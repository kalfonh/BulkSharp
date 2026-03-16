namespace BulkSharp.Core.Domain.Events;

public abstract class BulkOperationEvent
{
    public Guid OperationId { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public BulkOperationStatus Status { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class BulkOperationCreatedEvent : BulkOperationEvent
{
    public string? CreatedBy { get; init; }
    public string? FileName { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed class BulkOperationStatusChangedEvent : BulkOperationEvent
{
    public BulkOperationStatus PreviousStatus { get; init; }
}

public sealed class BulkOperationCompletedEvent : BulkOperationEvent
{
    public int TotalRows { get; init; }
    public int SuccessfulRows { get; init; }
    public int FailedRows { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class BulkOperationFailedEvent : BulkOperationEvent
{
    public string ErrorMessage { get; init; } = string.Empty;
    public int TotalRows { get; init; }
    public int ProcessedRows { get; init; }
}

public sealed class BulkRowFailedEvent : BulkOperationEvent
{
    public int RowIndex { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string ErrorType { get; init; } = string.Empty;
}
