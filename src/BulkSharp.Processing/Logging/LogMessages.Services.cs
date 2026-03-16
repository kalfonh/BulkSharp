namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── BulkOperationService ───────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating bulk operation for operation {OperationName}")]
    public static partial void CreatingBulkOperation(this ILogger logger, string operationName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to store file for operation {OperationId}")]
    public static partial void FileStorageFailed(this ILogger logger, Exception ex, Guid operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to schedule operation {OperationId}")]
    public static partial void SchedulingFailed(this ILogger logger, Exception ex, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created bulk operation {OperationId}")]
    public static partial void CreatedBulkOperation(this ILogger logger, Guid operationId);

    // ── BulkRetryService ───────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry submitted for operation {OperationId}: {RowCount} rows")]
    public static partial void RetrySubmitted(this ILogger logger, Guid operationId, int rowCount);

    // ── NullBulkScheduler ────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Operation {OperationId} left in Pending status (no local scheduler — expecting external Worker pickup)")]
    public static partial void OperationLeftPendingNoScheduler(this ILogger logger, Guid operationId);
}
