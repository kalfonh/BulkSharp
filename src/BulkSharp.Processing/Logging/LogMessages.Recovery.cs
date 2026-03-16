namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── OrphanedStepRecoveryService ────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "Orphaned step recovery is disabled")]
    public static partial void OrphanedStepRecoveryDisabled(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for orphaned signal-based step statuses...")]
    public static partial void CheckingForOrphanedSteps(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovered {Count} orphaned step(s) for operation {OperationId}")]
    public static partial void RecoveredOrphanedSteps(this ILogger logger, int count, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marked stuck Running operation {OperationId} as failed (started at {StartedAt})")]
    public static partial void MarkedStuckOperationFailed(this ILogger logger, Guid operationId, DateTime? startedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orphaned step recovery complete: {Count} row(s) transitioned to Failed")]
    public static partial void OrphanedStepRecoveryComplete(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "No orphaned signal-based steps found")]
    public static partial void NoOrphanedStepsFound(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orphaned step recovery skipped — database not ready yet. This is expected on first startup before migrations have run.")]
    public static partial void OrphanedStepRecoveryDbNotReady(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Orphaned step recovery failed — some rows may remain stuck in WaitingForCompletion")]
    public static partial void OrphanedStepRecoveryFailed(this ILogger logger, Exception ex);
}
