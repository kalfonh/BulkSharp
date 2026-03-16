using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Scheduling;

/// <summary>
/// A scheduler that accepts operations without processing them.
/// Operations remain in Pending status until a separate Worker process picks them up.
/// Used by <c>AddBulkSharpApi()</c> to avoid registering worker infrastructure in API-only processes.
/// </summary>
internal sealed class NullBulkScheduler(ILogger<NullBulkScheduler> logger) : IBulkScheduler
{
    public Task ScheduleBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        logger.OperationLeftPendingNoScheduler(bulkOperationId);
        return Task.CompletedTask;
    }

    public Task CancelBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
