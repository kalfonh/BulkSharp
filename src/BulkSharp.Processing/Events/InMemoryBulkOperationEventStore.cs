using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Contracts;

namespace BulkSharp.Processing.Events;

/// <summary>
/// In-memory event store with a bounded ring of recent events.
/// </summary>
/// <remarks>
/// Events exist to drive a UI, which only ever reads the recent tail, so the store is
/// capped and discards the oldest entries. Sequence numbers keep increasing across
/// eviction, so a client that falls behind sees a gap rather than silently re-reading.
/// <para>
/// Suitable for single-process hosts. A multi-instance deployment needs a shared store,
/// or each instance will serve only the events it happened to observe.
/// </para>
/// </remarks>
public sealed class InMemoryBulkOperationEventStore : IBulkOperationEventStore
{
    private const int MaxEvents = 1000;

    private readonly LinkedList<OperationEventDto> _events = new();
    private readonly object _gate = new();
    private long _sequence;

    /// <inheritdoc />
    public Task<OperationEventDto> AppendAsync(
        OperationEventDto operationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationEvent);

        lock (_gate)
        {
            var stored = operationEvent with { Sequence = ++_sequence };
            _events.AddLast(stored);

            while (_events.Count > MaxEvents)
                _events.RemoveFirst();

            return Task.FromResult(stored);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationEventDto>> GetForOperationAsync(
        Guid operationId,
        long? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Query(e => e.OperationId == operationId, since, limit));

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationEventDto>> GetAsync(
        long? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Query(_ => true, since, limit));

    private IReadOnlyList<OperationEventDto> Query(
        Func<OperationEventDto, bool> predicate,
        long? since,
        int limit)
    {
        var bounded = Math.Clamp(limit, 1, MaxEvents);

        lock (_gate)
        {
            return _events
                .Where(predicate)
                .Where(e => since is null || e.Sequence > since)
                .Take(bounded)
                .ToList();
        }
    }
}
