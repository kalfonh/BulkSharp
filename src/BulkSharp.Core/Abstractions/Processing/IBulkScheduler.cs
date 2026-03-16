namespace BulkSharp.Core.Abstractions.Processing;

/// <summary>Schedules bulk operations for asynchronous processing.</summary>
public interface IBulkScheduler
{
    Task ScheduleBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default);
    Task CancelBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default);
}
