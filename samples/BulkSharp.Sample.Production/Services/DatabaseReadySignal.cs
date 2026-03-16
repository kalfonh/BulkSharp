namespace BulkSharp.Sample.Production.Services;

/// <summary>
/// Simple signal that blocks consumers until the database is initialized.
/// Registered as a singleton so all services share the same instance.
/// </summary>
public sealed class DatabaseReadySignal
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _tcs.Task.IsCompletedSuccessfully;

    public void Signal() => _tcs.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _tcs.Task.WaitAsync(cancellationToken);
}
