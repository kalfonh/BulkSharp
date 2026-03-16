namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── BulkOperationProcessor ─────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing operation {OperationId}")]
    public static partial void ProcessingOperation(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation {OperationId} is already in terminal state {Status}, skipping")]
    public static partial void OperationInTerminalState(this ILogger logger, Guid operationId, BulkOperationStatus status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing operation {OperationId}")]
    public static partial void OperationProcessingError(this ILogger logger, Exception ex, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation {OperationId} was cancelled")]
    public static partial void OperationCancelled(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation {OperationId} is already running, skipping to prevent double-processing")]
    public static partial void OperationAlreadyRunning(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Operation {OperationId} not found")]
    public static partial void OperationNotFound(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist final state for operation {OperationId} — status may be stale in DB")]
    public static partial void FinalStatePersistFailed(this ILogger logger, Exception ex, Guid operationId);

    // ── TypedBulkOperationProcessor ────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing step-based operation {OperationId} with {StepCount} steps: {StepNames}")]
    public static partial void ProcessingStepBasedOperation(this ILogger logger, Guid operationId, int stepCount, string stepNames);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retry failed for row {RowNumber} in operation {OperationId}")]
    public static partial void RetryRowFailed(this ILogger logger, Exception ex, int rowNumber, Guid operationId);
}
