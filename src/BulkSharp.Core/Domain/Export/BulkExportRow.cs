using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Export;

public sealed class BulkExportRow
{
    public int RowNumber { get; init; }
    public string? RowId { get; init; }
    public RowRecordState State { get; init; }
    public string? StepName { get; init; }
    public int StepIndex { get; init; }
    public BulkErrorType? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryAttempt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? RowData { get; init; }
    public Type? RowType { get; init; }
}
