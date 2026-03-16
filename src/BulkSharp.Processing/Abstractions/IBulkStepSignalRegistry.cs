namespace BulkSharp.Processing.Abstractions;

/// <summary>
/// Internal registry for signal-mode async step waiters.
/// Allows registering and removing waiters independently of the completion handler,
/// so that a waiter can be registered before the DB write to avoid lost signals.
/// </summary>
internal interface IBulkStepSignalRegistry
{
    /// <summary>
    /// Registers a new waiter for the given signal key and returns its TaskCompletionSource.
    /// Throws if a waiter is already registered for this key.
    /// </summary>
    TaskCompletionSource<bool> RegisterWaiter(string signalKey);

    /// <summary>
    /// Returns an existing waiter for the given signal key, or registers a new one if none exists.
    /// </summary>
    TaskCompletionSource<bool> GetOrRegisterWaiter(string signalKey);

    /// <summary>
    /// Removes a waiter and cancels its TaskCompletionSource.
    /// Used for cleanup on timeout, external cancellation, or DB write failure.
    /// </summary>
    void RemoveWaiter(string signalKey);
}
