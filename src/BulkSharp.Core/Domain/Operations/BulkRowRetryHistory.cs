namespace BulkSharp.Core.Domain.Operations;

public sealed class BulkRowRetryHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BulkOperationId { get; set; }
    public int RowNumber { get; set; }
    public int StepIndex { get; set; }
    public int Attempt { get; set; }
    public BulkErrorType? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FailedAt { get; set; }
    public string? RowData { get; set; }

    public static BulkRowRetryHistory CreateFromRecord(BulkRowRecord record) => new()
    {
        BulkOperationId = record.BulkOperationId,
        RowNumber = record.RowNumber,
        StepIndex = record.StepIndex,
        Attempt = record.RetryAttempt,
        ErrorType = record.ErrorType,
        ErrorMessage = record.ErrorMessage,
        FailedAt = record.CompletedAt ?? DateTime.UtcNow,
        RowData = record.RowData
    };
}
