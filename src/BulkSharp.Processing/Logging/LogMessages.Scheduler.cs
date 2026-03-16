namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── ChannelsScheduler ──────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk operation {OperationId} scheduled via Channels")]
    public static partial void OperationScheduled(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot schedule operation {OperationId}: Channel is closed")]
    public static partial void ScheduleFailedChannelClosed(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot schedule operation {OperationId}: Queue is full")]
    public static partial void ScheduleFailedQueueFull(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancel requested for bulk operation {OperationId}")]
    public static partial void OperationCancelRequested(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting Channels scheduler with {WorkerCount} workers")]
    public static partial void SchedulerStarting(this ILogger logger, int workerCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channels scheduler started successfully")]
    public static partial void SchedulerStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping Channels scheduler...")]
    public static partial void SchedulerStopping(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graceful drain timed out, forcing cancellation")]
    public static partial void SchedulerDrainTimedOut(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channels scheduler stopped")]
    public static partial void SchedulerStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} started")]
    public static partial void WorkerStarted(this ILogger logger, int workerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {WorkerId} skipping cancelled operation {OperationId}")]
    public static partial void WorkerSkippingCancelledOperation(this ILogger logger, int workerId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {WorkerId} operation {OperationId} was cancelled")]
    public static partial void WorkerOperationCancelled(this ILogger logger, int workerId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} cancelled")]
    public static partial void WorkerCancelled(this ILogger logger, int workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} stopped")]
    public static partial void WorkerStopped(this ILogger logger, int workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered {Count} pending operations")]
    public static partial void PendingOperationsRecovered(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to recover pending operations on startup")]
    public static partial void PendingOperationsRecoveryFailed(this ILogger logger, Exception ex);

    // ── ImmediateScheduler ─────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "ImmediateScheduler is active — operations will be processed synchronously inline. This is intended for testing only.")]
    public static partial void ImmediateSchedulerActive(this ILogger logger);

    // ── Pending poll ─────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Pending operation poller started with interval {Interval}")]
    public static partial void PendingPollStarted(this ILogger logger, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pending poll cycle recovered {Count} new operations")]
    public static partial void PendingPollCycleComplete(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending poll cycle failed — will retry on next interval")]
    public static partial void PendingPollCycleFailed(this ILogger logger, Exception ex);

    // ── Stuck Running recovery ───────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovering stuck operation {OperationId} (started at {StartedAt})")]
    public static partial void RecoveringStuckOperation(this ILogger logger, Guid operationId, DateTime startedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered {Count} stuck Running operations")]
    public static partial void StuckOperationsRecovered(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to recover stuck Running operations")]
    public static partial void StuckOperationsRecoveryFailed(this ILogger logger, Exception ex);
}
