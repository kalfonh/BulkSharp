namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>
/// Service for signaling completion of signal-mode async bulk steps.
/// External callers use this to notify the processing pipeline that an async step has completed.
/// </summary>
public interface IBulkStepSignalService
{
    /// <summary>
    /// Signals completion for a signal-mode async step.
    /// Returns true if a waiter was found and signaled; false if the key is not registered.
    /// </summary>
    bool TrySignal(string signalKey);

    /// <summary>
    /// Signals failure for a signal-mode async step with an error message.
    /// The step will be marked as Failed and the error message recorded.
    /// Returns true if a waiter was found and signaled; false if the key is not registered.
    /// </summary>
    bool TrySignalFailure(string signalKey, string errorMessage);
}
