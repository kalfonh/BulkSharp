using BulkSharp.Core.Contracts;

namespace BulkSharp.Dashboard.Services;

/// <summary>
/// Raises toasts from the operation event feed.
/// </summary>
/// <remarks>
/// Replaces the previous approach of implementing <c>IBulkOperationEventHandler</c> and
/// injecting the UI's <see cref="ToastService"/> into it. That only worked when the UI and
/// the worker shared a process: events are dispatched inside the processing pipeline, so a
/// dashboard hosted separately — behind a gateway, or as a single-page application — saw
/// nothing.
/// <para>
/// Reading the feed over HTTP works in every topology, and is the same thing a front end in
/// any other technology stack would do.
/// </para>
/// </remarks>
/// <param name="api">Client for the BulkSharp API.</param>
/// <param name="toasts">The UI toast service.</param>
public sealed class OperationEventToastPoller(BulkSharpApiClient api, ToastService toasts)
{
    private readonly SemaphoreSlim _pumpGate = new(1, 1);
    private long _lastSequence;

    /// <summary>
    /// Fetches events newer than the last one seen and raises a toast for each.
    /// </summary>
    /// <remarks>
    /// Call from a component's polling timer. The sequence cursor advances only over events
    /// actually delivered, so nothing is shown twice and nothing is skipped.
    /// <para>
    /// Overlapping pumps are skipped rather than queued. A pump slower than the polling
    /// interval would otherwise run concurrently with the next one, both reading the cursor
    /// before either advanced it, and both raising the same toasts.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task PumpAsync(CancellationToken cancellationToken = default)
    {
        if (!await _pumpGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            var isFirstPump = _lastSequence == 0;

            var events = await api.GetEventsAsync(
                since: isFirstPump ? null : _lastSequence,
                cancellationToken: cancellationToken);

            foreach (var operationEvent in events)
            {
                // On the first pump, adopt the cursor without replaying history — an
                // operator opening the dashboard should not be shown every event since
                // startup.
                if (!isFirstPump)
                    toasts.Show(operationEvent.OperationName, operationEvent.Message, ToLevel(operationEvent.Severity));

                _lastSequence = Math.Max(_lastSequence, operationEvent.Sequence);
            }
        }
        finally
        {
            _pumpGate.Release();
        }
    }

    private static ToastLevel ToLevel(OperationEventSeverity severity) => severity switch
    {
        OperationEventSeverity.Error => ToastLevel.Error,
        OperationEventSeverity.Warning => ToastLevel.Warning,
        _ => ToastLevel.Info
    };
}
