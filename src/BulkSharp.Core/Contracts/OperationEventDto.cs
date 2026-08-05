namespace BulkSharp.Core.Contracts;

/// <summary>Severity of an operation event, for rendering in a UI.</summary>
public enum OperationEventSeverity
{
    /// <summary>Progress or lifecycle information.</summary>
    Info,

    /// <summary>Completed, but with failed rows.</summary>
    Warning,

    /// <summary>The operation or a row failed.</summary>
    Error
}

/// <summary>
/// A notable moment in an operation's lifecycle, readable over HTTP.
/// </summary>
/// <remarks>
/// Operation events are dispatched inside the processing pipeline, so a UI in another
/// process — a gateway-hosted dashboard, or any single-page application — never observes
/// them. Persisting events and exposing them as a feed is what lets any front end render
/// notifications, rather than only a UI that happens to share the worker's process.
/// <para>
/// This is distinct from <c>IBulkNotificationChannel</c>. Channels deliver outward (email,
/// SMS, webhooks) and correctly execute where processing happens; this feed is what a UI
/// reads back.
/// </para>
/// </remarks>
/// <param name="Sequence">
/// Monotonically increasing per store. Clients poll with <c>?since=</c> and pass the
/// highest sequence they have seen, so no event is delivered twice or missed.
/// </param>
/// <param name="OperationId">The operation the event belongs to.</param>
/// <param name="OperationName">Name of the operation, for display without a second lookup.</param>
/// <param name="Type">Event type, such as <c>Completed</c>, <c>Failed</c> or <c>StatusChanged</c>.</param>
/// <param name="Severity">How the event should be surfaced.</param>
/// <param name="Message">Human-readable summary.</param>
/// <param name="Timestamp">When the event occurred, in UTC.</param>
public sealed record OperationEventDto(
    long Sequence,
    Guid OperationId,
    string OperationName,
    string Type,
    OperationEventSeverity Severity,
    string Message,
    DateTime Timestamp);
