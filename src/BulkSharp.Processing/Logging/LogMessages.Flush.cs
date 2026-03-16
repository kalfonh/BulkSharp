namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── RowRecordFlushService ────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to flush {Count} row record creates")]
    public static partial void RowRecordCreateFlushFailed(this ILogger logger, Exception ex, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to flush {Count} row record updates")]
    public static partial void RowRecordUpdateFlushFailed(this ILogger logger, Exception ex, int count);

    // ── Worker lifecycle (shared by ChannelsScheduler workers) ────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} processing operation {OperationId}")]
    public static partial void WorkerProcessing(this ILogger logger, int workerId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} completed operation {OperationId}")]
    public static partial void WorkerCompleted(this ILogger logger, int workerId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Worker {WorkerId} error processing operation {OperationId}")]
    public static partial void WorkerError(this ILogger logger, Exception ex, int workerId, Guid operationId);
}
