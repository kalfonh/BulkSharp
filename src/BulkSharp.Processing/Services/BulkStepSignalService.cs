namespace BulkSharp.Processing.Services;

/// <summary>
/// In-process registry for signal-mode async step coordination.
/// Registered as a singleton so that signal producers (API endpoints) and
/// signal consumers (step executor) share the same waiter dictionary.
/// </summary>
internal sealed class BulkStepSignalService : IBulkStepSignalService, IBulkStepSignalRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waiters = new();

    /// <summary>
    /// Registers a waiter for the given signal key and returns its TaskCompletionSource.
    /// Called by the step executor when entering signal-wait mode.
    /// </summary>
    public TaskCompletionSource<bool> RegisterWaiter(string signalKey)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_waiters.TryAdd(signalKey, tcs))
            throw new InvalidOperationException($"A waiter for signal key '{signalKey}' is already registered.");
        return tcs;
    }

    /// <inheritdoc />
    public bool TrySignal(string signalKey)
    {
        if (_waiters.TryRemove(signalKey, out var tcs))
        {
            tcs.TrySetResult(true);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool TrySignalFailure(string signalKey, string errorMessage)
    {
        if (_waiters.TryRemove(signalKey, out var tcs))
        {
            tcs.TrySetException(new BulkStepSignalFailureException(signalKey, errorMessage));
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public TaskCompletionSource<bool> GetOrRegisterWaiter(string signalKey)
    {
        return _waiters.GetOrAdd(signalKey, _ =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>
    /// Removes a waiter and cancels its TaskCompletionSource. Used for cleanup
    /// on timeout or external cancellation.
    /// </summary>
    public void RemoveWaiter(string signalKey)
    {
        if (_waiters.TryRemove(signalKey, out var tcs))
            tcs.TrySetCanceled();
    }
}
