namespace BulkSharp.Core.Domain.Operations;

public sealed class BulkRowRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BulkOperationId { get; set; }
    public int RowNumber { get; set; }
    public string? RowId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public int StepIndex { get; set; }
    public RowRecordState State { get; set; } = RowRecordState.Pending;
    public BulkErrorType? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RowData { get; set; }
    public string? SignalKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RetryAttempt { get; set; }
    public int? RetryFromStepIndex { get; set; }

    public void MarkRunning()
    {
        State = RowRecordState.Running;
        StartedAt = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        State = RowRecordState.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string errorMessage, BulkErrorType errorType = BulkErrorType.Processing)
    {
        State = RowRecordState.Failed;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkTimedOut(string stepName)
    {
        State = RowRecordState.TimedOut;
        ErrorType = BulkErrorType.Timeout;
        ErrorMessage = $"Step '{stepName}' timed out waiting for completion";
        CompletedAt = DateTime.UtcNow;
    }

    public void ResetForRetry(int fromStepIndex)
    {
        State = RowRecordState.Pending;
        RetryAttempt++;
        RetryFromStepIndex = fromStepIndex;
        ErrorType = null;
        ErrorMessage = null;
        CompletedAt = null;
    }

    public void MarkWaitingForCompletion()
    {
        State = RowRecordState.WaitingForCompletion;
    }

    public static BulkRowRecord CreateValidation(Guid operationId, int rowNumber, string? rowId = null, string? rowData = null)
    {
        return new BulkRowRecord
        {
            BulkOperationId = operationId,
            RowNumber = rowNumber,
            RowId = rowId,
            StepName = "validation",
            StepIndex = -1,
            State = RowRecordState.Pending,
            RowData = rowData,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static BulkRowRecord CreateStep(Guid operationId, int rowNumber, string? rowId, string stepName, int stepIndex)
    {
        return new BulkRowRecord
        {
            BulkOperationId = operationId,
            RowNumber = rowNumber,
            RowId = rowId,
            StepName = stepName,
            StepIndex = stepIndex,
            State = RowRecordState.Running,
            StartedAt = DateTime.UtcNow
        };
    }
}
