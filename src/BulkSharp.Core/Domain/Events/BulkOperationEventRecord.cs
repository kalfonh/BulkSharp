using BulkSharp.Core.Contracts;

namespace BulkSharp.Core.Domain.Events;

/// <summary>
/// Persisted form of an operation event.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="OperationEventDto"/> so the wire contract and the storage
/// schema can evolve independently, and because a positional record is awkward to map.
/// <para>
/// <see cref="Sequence"/> is assigned by the store, not the caller. A durable store backs it
/// with an identity column, which is what makes the value monotonic across every instance of
/// a horizontally scaled service — the property the in-memory store cannot provide.
/// </para>
/// </remarks>
public sealed class BulkOperationEventRecord
{
    /// <summary>Store-assigned monotonic sequence. Clients poll with the highest value seen.</summary>
    public long Sequence { get; set; }

    /// <summary>The operation this event belongs to.</summary>
    public Guid OperationId { get; set; }

    /// <summary>Operation name, denormalized so a feed can be rendered without a second lookup.</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>Event type, such as <c>Completed</c>, <c>Failed</c> or <c>StatusChanged</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>How the event should be surfaced.</summary>
    public OperationEventSeverity Severity { get; set; }

    /// <summary>Human-readable summary.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>When the event occurred, in UTC.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Projects to the wire contract.</summary>
    public OperationEventDto ToDto() => new(
        Sequence,
        OperationId,
        OperationName,
        Type,
        Severity,
        Message,
        Timestamp);

    /// <summary>Creates a record from the wire contract. Any supplied sequence is discarded.</summary>
    /// <param name="dto">The event to persist.</param>
    public static BulkOperationEventRecord FromDto(OperationEventDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new BulkOperationEventRecord
        {
            OperationId = dto.OperationId,
            OperationName = dto.OperationName,
            Type = dto.Type,
            Severity = dto.Severity,
            Message = dto.Message,
            Timestamp = dto.Timestamp
        };
    }
}
