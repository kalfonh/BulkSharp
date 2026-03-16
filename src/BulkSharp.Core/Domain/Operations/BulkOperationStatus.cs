namespace BulkSharp.Core.Domain.Operations;

/// <summary>The lifecycle status of a bulk operation.</summary>
public enum BulkOperationStatus
{
    Pending,
    Validating,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled
}
