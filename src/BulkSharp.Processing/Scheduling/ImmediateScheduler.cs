using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Scheduling;

/// <summary>
/// A scheduler that processes operations immediately inline, blocking the caller for the
/// full duration of the operation. <c>ScheduleBulkOperationAsync</c> does not return until
/// all rows have been processed.
/// <para>
/// <b>Intended for unit and integration tests only.</b> In production, use
/// <see cref="ChannelsScheduler"/> which processes operations asynchronously on background workers.
/// </para>
/// </summary>
internal sealed class ImmediateScheduler : IBulkScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImmediateScheduler> _logger;

    public ImmediateScheduler(IServiceProvider serviceProvider, ILogger<ImmediateScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _logger.ImmediateSchedulerActive();
    }

    public async Task ScheduleBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IBulkOperationProcessor>();
        await processor.ProcessOperationAsync(bulkOperationId, cancellationToken).ConfigureAwait(false);
    }

    public Task CancelBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        // Immediate execution means cancel is a no-op — processing completes before this could be called
        return Task.CompletedTask;
    }
}
