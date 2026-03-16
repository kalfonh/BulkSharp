using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class BulkStepExecutorService : IBulkStepExecutor
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly ILogger<BulkStepExecutorService> _logger;
    private readonly IBulkStepSignalRegistry _signalRegistry;
    private readonly Dictionary<StepCompletionMode, IAsyncStepCompletionHandler> _handlers;

    public BulkStepExecutorService(
        ILogger<BulkStepExecutorService> logger,
        IBulkStepSignalRegistry signalRegistry,
        IEnumerable<IAsyncStepCompletionHandler> handlers)
    {
        _logger = logger;
        _signalRegistry = signalRegistry;
        _handlers = handlers.ToDictionary(h => h.Mode);
    }

    public async Task ExecuteStepAsync<TMetadata, TRow>(
        IBulkStep<TMetadata, TRow> step,
        TRow row,
        TMetadata metadata,
        CancellationToken cancellationToken = default)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        await ExecuteWithRetryAsync(step, row, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteStepAsync<TMetadata, TRow>(
        IBulkStep<TMetadata, TRow> step,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record,
        IBulkStepRecordManager recordManager,
        CancellationToken cancellationToken = default)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        try
        {
            await ExecuteWithRetryAsync(step, row, metadata, cancellationToken).ConfigureAwait(false);

            if (step is IAsyncBulkStep<TMetadata, TRow> asyncStep)
            {
                await HandleAsyncCompletionAsync(asyncStep, row, metadata, record, recordManager, cancellationToken).ConfigureAwait(false);
            }

            record.MarkCompleted();
        }
        catch (BulkStepTimeoutException)
        {
            record.MarkTimedOut(step.Name);
            await recordManager.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (BulkStepSignalFailureException ex)
        {
            record.MarkFailed(ex.Message, BulkErrorType.SignalFailure);
            await recordManager.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            record.MarkFailed(ex.Message, BulkErrorType.StepFailure);
            await recordManager.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await recordManager.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleAsyncCompletionAsync<TMetadata, TRow>(
        IAsyncBulkStep<TMetadata, TRow> asyncStep,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record,
        IBulkStepRecordManager recordManager,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        if (!_handlers.TryGetValue(asyncStep.CompletionMode, out var handler))
            throw new InvalidOperationException($"Unknown completion mode: {asyncStep.CompletionMode}");

        record.MarkWaitingForCompletion();

        // For signal-based steps, populate SignalKey before persisting so external
        // services (e.g. DemoSignalService) can discover it while the step is waiting.
        handler.PrepareStatus(asyncStep, row, metadata, record);

        // Register the waiter BEFORE the DB write so that an external signal arriving
        // between the write and the wait is not lost.
        string? signalKey = null;
        if (asyncStep.CompletionMode == StepCompletionMode.Signal && record.SignalKey is not null)
        {
            signalKey = record.SignalKey;
            _signalRegistry.RegisterWaiter(signalKey);
        }

        try
        {
            // WaitingForCompletion must be written immediately — external systems
            // discover rows to signal by querying this state in the DB.
            await recordManager.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // DB write failed — clean up the pre-registered waiter to avoid leaks.
            if (signalKey is not null)
                _signalRegistry.RemoveWaiter(signalKey);
            throw;
        }

        await handler.WaitForCompletionAsync(asyncStep, row, metadata, record, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteWithRetryAsync<TMetadata, TRow>(
        IBulkStep<TMetadata, TRow> step,
        TRow row,
        TMetadata metadata,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        for (int attempt = 0; attempt <= step.MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.RetryingStep(step.Name, attempt, step.MaxRetries);
                    var baseDelay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), MaxRetryDelay.TotalSeconds));
                    var jitter = Random.Shared.Next(0, (int)(baseDelay.TotalMilliseconds * 0.5));
                    await Task.Delay(baseDelay + TimeSpan.FromMilliseconds(jitter), cancellationToken).ConfigureAwait(false);
                }

                await step.ExecuteAsync(row, metadata, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < step.MaxRetries)
            {
                _logger.StepFailedWillRetry(ex, step.Name, attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.StepFailedAllRetries(ex, step.Name, step.MaxRetries);
                throw;
            }
        }
    }
}
