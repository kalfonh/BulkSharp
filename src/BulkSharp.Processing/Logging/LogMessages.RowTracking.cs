namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting Validating phase for operation {OperationId}")]
    public static partial void ValidatingPhaseStarted(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Validating phase complete for operation {OperationId}: {TotalRows} rows, {FailedRows} failed validation")]
    public static partial void ValidatingPhaseComplete(this ILogger logger, Guid operationId, int totalRows, int failedRows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transitioning operation {OperationId} from Validating to Processing")]
    public static partial void TransitioningToProcessing(this ILogger logger, Guid operationId);
}
