using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Contracts;
using BulkSharp.Core.Domain.Events;

namespace BulkSharp.Processing.Events;

/// <summary>
/// Persists operation events so a user interface in any process can read them back.
/// </summary>
/// <remarks>
/// Runs alongside the notification channels rather than replacing them: channels deliver
/// outward, this records what happened for a UI to render.
/// </remarks>
/// <param name="store">The event store to append to.</param>
internal sealed class EventStoreEventHandler(IBulkOperationEventStore store) : IBulkOperationEventHandler
{
    /// <inheritdoc />
    public Task OnOperationCreatedAsync(BulkOperationCreatedEvent e, CancellationToken ct)
        => AppendAsync(e, "Created", OperationEventSeverity.Info, "Operation created", ct);

    /// <inheritdoc />
    public Task OnStatusChangedAsync(BulkOperationStatusChangedEvent e, CancellationToken ct)
        => AppendAsync(e, "StatusChanged", OperationEventSeverity.Info,
            $"{e.PreviousStatus} to {e.Status}", ct);

    /// <inheritdoc />
    public Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
        => AppendAsync(
            e,
            "Completed",
            e.FailedRows > 0 ? OperationEventSeverity.Warning : OperationEventSeverity.Info,
            $"{e.SuccessfulRows}/{e.TotalRows} rows succeeded in {e.Duration:g}",
            ct);

    /// <inheritdoc />
    public Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct)
        => AppendAsync(e, "Failed", OperationEventSeverity.Error, e.ErrorMessage, ct);

    /// <inheritdoc />
    public Task OnRowFailedAsync(BulkRowFailedEvent e, CancellationToken ct)
        => AppendAsync(e, "RowFailed", OperationEventSeverity.Error,
            $"Row {e.RowIndex}: {e.ErrorMessage}", ct);

    private Task AppendAsync(
        BulkOperationEvent e,
        string type,
        OperationEventSeverity severity,
        string message,
        CancellationToken ct)
        => store.AppendAsync(
            new OperationEventDto(
                Sequence: 0,
                e.OperationId,
                e.OperationName,
                type,
                severity,
                message,
                e.Timestamp),
            ct);
}
