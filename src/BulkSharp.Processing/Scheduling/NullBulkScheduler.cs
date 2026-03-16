using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Scheduling;

/// <summary>
/// A scheduler that accepts operations without processing them.
/// Operations remain in Pending status until a separate Worker process picks them up.
/// Used by <c>AddBulkSharpApi()</c> to avoid registering worker infrastructure in API-only processes.
/// </summary>
internal sealed class NullBulkScheduler : IBulkScheduler
{
    private readonly ILogger<NullBulkScheduler> _logger;

    public NullBulkScheduler(ILogger<NullBulkScheduler> logger)
    {
        _logger = logger;
    }

    public Task ScheduleBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        _logger.OperationLeftPendingNoScheduler(bulkOperationId);
        return Task.CompletedTask;
    }

    public Task CancelBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
