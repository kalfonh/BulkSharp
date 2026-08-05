using BulkSharp.Core.Contracts;

namespace BulkSharp.Core.Abstractions.Events;

/// <summary>
/// Stores operation events so they can be read back over HTTP by a user interface.
/// </summary>
/// <remarks>
/// Implementations must assign a monotonically increasing sequence per store, so a client
/// polling with <c>since</c> neither misses nor repeats an event.
/// </remarks>
public interface IBulkOperationEventStore
{
    /// <summary>Records an event and assigns it the next sequence number.</summary>
    /// <param name="operationEvent">The event to record. <c>Sequence</c> is ignored and overwritten.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stored event, with its assigned sequence.</returns>
    Task<OperationEventDto> AppendAsync(
        OperationEventDto operationEvent,
        CancellationToken cancellationToken = default);

    /// <summary>Returns events for one operation, in sequence order.</summary>
    /// <param name="operationId">The operation to read events for.</param>
    /// <param name="since">Return only events with a sequence greater than this. Null returns all.</param>
    /// <param name="limit">Maximum number of events to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<IReadOnlyList<OperationEventDto>> GetForOperationAsync(
        Guid operationId,
        long? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Returns events across all operations, in sequence order.</summary>
    /// <param name="since">Return only events with a sequence greater than this. Null returns all.</param>
    /// <param name="limit">Maximum number of events to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<IReadOnlyList<OperationEventDto>> GetAsync(
        long? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
