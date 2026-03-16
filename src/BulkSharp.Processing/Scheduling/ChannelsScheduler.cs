using BulkSharp.Core.Domain.Queries;
using BulkSharp.Processing.Logging;
using Microsoft.Extensions.Hosting;

namespace BulkSharp.Processing.Scheduling;

/// <summary>
/// High-performance scheduler using System.Threading.Channels for queue management
/// with configurable concurrency and backpressure handling.
/// </summary>
/// <remarks>
/// Implements <see cref="IHostedService"/> directly instead of <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// because startup and shutdown have asymmetric logic that does not fit the single-method ExecuteAsync model:
/// StartAsync launches N workers + recovers pending operations from the database;
/// StopAsync drains the queue with a configurable timeout, then force-cancels remaining work.
/// </remarks>
internal sealed class ChannelsScheduler : IBulkScheduler, IHostedService, IAsyncDisposable, IDisposable
{
    private readonly ILogger<ChannelsScheduler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<SchedulerWorkItem> _queue;
    private readonly ChannelsSchedulerOptions _options;
    private readonly List<Task> _workers = new();
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _operationTokens = new();
    private readonly ConcurrentDictionary<Guid, byte> _cancelledOperations = new();
    private readonly ConcurrentDictionary<Guid, byte> _enqueuedOperations = new();
    private readonly CancellationTokenSource _pollerTokenSource = new();
    private Task? _pollerTask;

    public ChannelsScheduler(
        ILogger<ChannelsScheduler> logger,
        IServiceProvider serviceProvider,
        IOptions<ChannelsSchedulerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;

        // Create channel with bounded capacity for backpressure
        var channelOptions = new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = _options.FullMode,
            SingleReader = false,
            SingleWriter = false
        };

        _queue = Channel.CreateBounded<SchedulerWorkItem>(channelOptions);
    }

    public async Task ScheduleBulkOperationAsync(
        Guid bulkOperationId,
        CancellationToken cancellationToken = default)
    {
        var workItem = new SchedulerWorkItem
        {
            OperationId = bulkOperationId,
            EnqueuedAt = DateTime.UtcNow
        };

        try
        {
            _enqueuedOperations.TryAdd(bulkOperationId, 0);
            await _queue.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
            _logger.OperationScheduled(bulkOperationId);
        }
        catch (ChannelClosedException)
        {
            _logger.ScheduleFailedChannelClosed(bulkOperationId);
            throw new InvalidOperationException("Scheduler is shutting down");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("full", StringComparison.OrdinalIgnoreCase))
        {
            _logger.ScheduleFailedQueueFull(bulkOperationId);
            throw new InvalidOperationException("Scheduler queue is at capacity");
        }
    }

    public Task CancelBulkOperationAsync(
        Guid bulkOperationId,
        CancellationToken cancellationToken = default)
    {
        _logger.OperationCancelRequested(bulkOperationId);

        if (_operationTokens.TryGetValue(bulkOperationId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Operation already completed and disposed its CTS
            }
        }

        // Mark as cancelled so dequeue logic skips it if not yet picked up
        _cancelledOperations.TryAdd(bulkOperationId, 0);
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.SchedulerStarting(_options.WorkerCount);

        for (int i = 0; i < _options.WorkerCount; i++)
        {
            var workerId = i;
            var workerTask = RunWorkerAsync(workerId, _shutdownTokenSource.Token);
            _workers.Add(workerTask);
        }

        await RecoverStuckRunningOperationsAsync(cancellationToken).ConfigureAwait(false);
        await RecoverPendingOperationsAsync(cancellationToken).ConfigureAwait(false);

        if (_options.PendingPollInterval.HasValue)
        {
            _logger.PendingPollStarted(_options.PendingPollInterval.Value);
            _pollerTask = RunPollerAsync(_options.PendingPollInterval.Value, _pollerTokenSource.Token);
        }

        _logger.SchedulerStarted();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.SchedulerStopping();
        _queue.Writer.TryComplete();

        try
        {
            _pollerTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        var tasksToAwait = new List<Task>(_workers);
        if (_pollerTask != null)
            tasksToAwait.Add(_pollerTask);

        var drainTask = Task.WhenAll(tasksToAwait);
        var completed = await Task.WhenAny(drainTask, Task.Delay(_options.ShutdownTimeout, CancellationToken.None)).ConfigureAwait(false);

        if (completed != drainTask)
        {
            _logger.SchedulerDrainTimedOut();
            _shutdownTokenSource.Cancel();
            await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)).ConfigureAwait(false);
        }

        _logger.SchedulerStopped();
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        _logger.WorkerStarted(workerId);

        try
        {
            await foreach (var workItem in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _operationTokens[workItem.OperationId] = cts;

                // Re-check: cancel may have arrived between CTS creation and registration
                if (_cancelledOperations.TryRemove(workItem.OperationId, out _))
                {
                    _logger.WorkerSkippingCancelledOperation(workerId, workItem.OperationId);
                    _operationTokens.TryRemove(workItem.OperationId, out _);
                    cts.Dispose();
                    continue;
                }

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IBulkOperationProcessor>();

                    _logger.WorkerProcessing(workerId, workItem.OperationId);

                    await processor.ProcessOperationAsync(workItem.OperationId, cts.Token).ConfigureAwait(false);

                    _logger.WorkerCompleted(workerId, workItem.OperationId);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger.WorkerOperationCancelled(workerId, workItem.OperationId);
                }
                catch (Exception ex)
                {
                    _logger.WorkerError(ex, workerId, workItem.OperationId);
                }
                finally
                {
                    _operationTokens.TryRemove(workItem.OperationId, out _);
                    _cancelledOperations.TryRemove(workItem.OperationId, out _);
                    _enqueuedOperations.TryRemove(workItem.OperationId, out _);

                    try
                    {
                        cts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.WorkerCancelled(workerId);
        }
        finally
        {
            _logger.WorkerStopped(workerId);
        }
    }

    /// <summary>
    /// Queries the repository for operations still in Pending status and re-enqueues them.
    /// Recovers from crash/restart scenarios.
    /// </summary>
    private async Task RecoverPendingOperationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();

            var recoveredCount = 0;
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var query = new BulkOperationQuery
                {
                    Status = BulkOperationStatus.Pending,
                    Page = page,
                    PageSize = pageSize,
                    SortBy = "CreatedAt",
                    SortDescending = false
                };

                var result = await repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);

                foreach (var operation in result.Items)
                {
                    if (!_enqueuedOperations.TryAdd(operation.Id, 0))
                        continue;

                    await ScheduleBulkOperationAsync(operation.Id, cancellationToken).ConfigureAwait(false);
                    recoveredCount++;
                }

                if (!result.HasNextPage)
                    break;

                page++;
            }

            if (recoveredCount > 0)
                _logger.PendingOperationsRecovered(recoveredCount);
        }
        catch (Exception ex)
        {
            // Recovery is best-effort; don't prevent startup
            _logger.PendingOperationsRecoveryFailed(ex);
        }
    }

    /// <summary>
    /// Finds operations stuck in Running status beyond <see cref="ChannelsSchedulerOptions.StuckOperationTimeout"/>,
    /// marks them Failed, and re-creates them as new Pending operations for reprocessing.
    /// </summary>
    private async Task RecoverStuckRunningOperationsAsync(CancellationToken cancellationToken)
    {
        if (!_options.StuckOperationTimeout.HasValue)
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();

            var cutoff = DateTime.UtcNow - _options.StuckOperationTimeout.Value;
            var recoveredCount = 0;
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var query = new BulkOperationQuery
                {
                    Status = BulkOperationStatus.Running,
                    Page = page,
                    PageSize = pageSize,
                    SortBy = "CreatedAt",
                    SortDescending = false
                };

                var result = await repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);

                foreach (var operation in result.Items)
                {
                    if (operation.StartedAt == null || operation.StartedAt >= cutoff)
                        continue;

                    _logger.RecoveringStuckOperation(operation.Id, operation.StartedAt.Value);

                    operation.MarkFailed("Operation was stuck in Running state — recovered by scheduler after timeout");
                    await repository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
                    recoveredCount++;
                }

                if (!result.HasNextPage)
                    break;

                page++;
            }

            if (recoveredCount > 0)
                _logger.StuckOperationsRecovered(recoveredCount);
        }
        catch (Exception ex)
        {
            _logger.StuckOperationsRecoveryFailed(ex);
        }
    }

    private async Task RunPollerAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                await RecoverStuckRunningOperationsAsync(cancellationToken).ConfigureAwait(false);
                await RecoverPendingOperationsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Don't let a transient failure kill the poller — log and retry on next cycle
                _logger.PendingPollCycleFailed(ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_shutdownTokenSource.IsCancellationRequested)
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _shutdownTokenSource.Dispose();
        _pollerTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (!_shutdownTokenSource.IsCancellationRequested)
        {
            try
            {
                _shutdownTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }
        }

        _shutdownTokenSource.Dispose();
        _pollerTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
