namespace BulkSharp.Processing.Models;

internal readonly record struct SchedulerWorkItem
{
    public required Guid OperationId { get; init; }
    public required DateTime EnqueuedAt { get; init; }
}
