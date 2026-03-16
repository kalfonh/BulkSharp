using BulkSharp.Core.Domain.Events;

namespace BulkSharp.Core.Domain.Operations;

/// <summary>
/// Represents a bulk processing operation with status tracking and row counts.
/// Properties use public setters because this type crosses HTTP/JSON serialization boundaries
/// (Dashboard API). Use the MarkXxx() methods and RecordRowResult() for state transitions —
/// do not set Status, counters, or timestamps directly outside the processing pipeline.
/// </summary>
public sealed class BulkOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OperationName { get; set; } = string.Empty;
    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public BulkOperationStatus Status { get; set; } = BulkOperationStatus.Pending;
    private int _totalRows;
    private int _processedRows;
    private int _successfulRows;
    private int _failedRows;

    public int TotalRows { get => _totalRows; set => _totalRows = value; }
    public int ProcessedRows { get => _processedRows; set => _processedRows = value; }
    public int SuccessfulRows { get => _successfulRows; set => _successfulRows = value; }
    public int FailedRows { get => _failedRows; set => _failedRows = value; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public long FileSize { get; set; }
    public int ErrorCount => FailedRows;

    /// <summary>
    /// Concurrency token for optimistic concurrency control.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>Transitions the operation to Validating state. Only valid from Pending.</summary>
    public void MarkValidating()
    {
        if (Status != BulkOperationStatus.Pending)
            throw new InvalidOperationException($"Cannot transition from {Status} to Validating");

        Status = BulkOperationStatus.Validating;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions the operation to Running state. Only valid from Pending or Validating.</summary>
    public void MarkRunning()
    {
        if (Status is not (BulkOperationStatus.Pending or BulkOperationStatus.Validating or BulkOperationStatus.Retrying))
            throw new InvalidOperationException($"Cannot transition from {Status} to Running");
        Status = BulkOperationStatus.Running;
        if (StartedAt == null)
            StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Transitions the operation to Completed or CompletedWithErrors state based on row results.
    /// Only valid from Running.
    /// </summary>
    public void MarkCompleted()
    {
        if (Status != BulkOperationStatus.Running)
            throw new InvalidOperationException($"Cannot transition from {Status} to Completed");
        Status = Volatile.Read(ref _failedRows) > 0 ? BulkOperationStatus.CompletedWithErrors : BulkOperationStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions the operation to Failed state. Returns early if already in a terminal state.</summary>
    public void MarkFailed(string errorMessage)
    {
        if (Status is BulkOperationStatus.Completed or BulkOperationStatus.CompletedWithErrors
            or BulkOperationStatus.Failed or BulkOperationStatus.Cancelled)
            return;
        Status = BulkOperationStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions the operation to Cancelled state. Only valid from Pending or Running.</summary>
    public void MarkCancelled()
    {
        if (Status is not (BulkOperationStatus.Pending or BulkOperationStatus.Validating or BulkOperationStatus.Running))
            throw new InvalidOperationException($"Cannot cancel operation in {Status} state");
        Status = BulkOperationStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns a terminal event matching the current status, or null if the operation is not in a terminal state.
    /// </summary>
    public BulkOperationEvent? ToTerminalEvent() => Status switch
    {
        BulkOperationStatus.Completed or BulkOperationStatus.CompletedWithErrors =>
            new BulkOperationCompletedEvent
            {
                OperationId = Id,
                OperationName = OperationName,
                Status = Status,
                TotalRows = TotalRows,
                SuccessfulRows = SuccessfulRows,
                FailedRows = FailedRows,
                Duration = CompletedAt.HasValue && StartedAt.HasValue
                    ? CompletedAt.Value - StartedAt.Value
                    : TimeSpan.Zero
            },
        BulkOperationStatus.Failed =>
            new BulkOperationFailedEvent
            {
                OperationId = Id,
                OperationName = OperationName,
                Status = BulkOperationStatus.Failed,
                ErrorMessage = ErrorMessage ?? string.Empty,
                TotalRows = TotalRows,
                ProcessedRows = ProcessedRows
            },
        _ => null
    };

    /// <summary>Transitions the operation to Retrying state. Only valid from CompletedWithErrors.</summary>
    public void MarkRetrying()
    {
        if (Status != BulkOperationStatus.CompletedWithErrors)
            throw new InvalidOperationException($"Cannot transition from {Status} to Retrying");
        Status = BulkOperationStatus.Retrying;
        RetryCount++;
        CompletedAt = null;
    }

    /// <summary>Recalculates row counters from actual row record states. Used after retry.</summary>
    public void RecalculateCounters(int successCount, int failCount, int processedCount)
    {
        Interlocked.Exchange(ref _successfulRows, successCount);
        Interlocked.Exchange(ref _failedRows, failCount);
        Interlocked.Exchange(ref _processedRows, processedCount);
    }

    /// <summary>Sets the total number of rows to process. Should be called once after counting rows from the file.</summary>
    public void SetTotalRows(int count)
    {
        Interlocked.Exchange(ref _totalRows, count);
    }

    /// <summary>Records the result of processing a single row, incrementing the appropriate counters.</summary>
    public void RecordRowResult(bool success)
    {
        Interlocked.Increment(ref _processedRows);
        if (success)
            Interlocked.Increment(ref _successfulRows);
        else
            Interlocked.Increment(ref _failedRows);
    }
}
