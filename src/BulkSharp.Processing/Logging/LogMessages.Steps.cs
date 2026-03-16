namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── BulkStepExecutorService ────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Error, Message = "Step '{StepName}' failed for row {RowNumber}")]
    public static partial void StepExecutionFailed(this ILogger logger, string stepName, int rowNumber, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Async step '{StepName}' timed out waiting for signal '{SignalKey}'")]
    public static partial void AsyncStepTimeout(this ILogger logger, string stepName, string signalKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retrying step {StepName}, attempt {Attempt}/{MaxRetries}")]
    public static partial void RetryingStep(this ILogger logger, string stepName, int attempt, int maxRetries);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Step {StepName} failed on attempt {Attempt}, will retry")]
    public static partial void StepFailedWillRetry(this ILogger logger, Exception ex, string stepName, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Step {StepName} failed after {MaxRetries} retries")]
    public static partial void StepFailedAllRetries(this ILogger logger, Exception ex, string stepName, int maxRetries);

    // ── SignalCompletionHandler ─────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "Signal step '{StepName}' waiting for signal key '{SignalKey}' with timeout {TimeoutSeconds}s")]
    public static partial void SignalStepWaiting(this ILogger logger, string stepName, string signalKey, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Signal step '{StepName}' received signal for key '{SignalKey}'")]
    public static partial void SignalStepReceived(this ILogger logger, string stepName, string signalKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signal step '{StepName}' failed via external signal for key '{SignalKey}': {Error}")]
    public static partial void SignalStepFailed(this ILogger logger, string stepName, string signalKey, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signal received for key '{SignalKey}', row {RowNumber}")]
    public static partial void SignalReceived(this ILogger logger, string signalKey, int rowNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Signal failed for key '{SignalKey}', row {RowNumber}: {ErrorMessage}")]
    public static partial void SignalFailed(this ILogger logger, string signalKey, int rowNumber, string errorMessage);

    // ── PollingCompletionHandler ────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "Polling step '{StepName}' every {IntervalSeconds}s with timeout {TimeoutSeconds}s")]
    public static partial void PollingStepStarted(this ILogger logger, string stepName, double intervalSeconds, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Polling step '{StepName}' completed")]
    public static partial void PollingStepCompleted(this ILogger logger, string stepName);
}
