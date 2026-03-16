using BulkSharp.Core.Domain.Events;

namespace BulkSharp.Core.Abstractions.Events;

public interface IBulkOperationEventHandler
{
    Task OnOperationCreatedAsync(BulkOperationCreatedEvent e, CancellationToken ct) => Task.CompletedTask;
    Task OnStatusChangedAsync(BulkOperationStatusChangedEvent e, CancellationToken ct) => Task.CompletedTask;
    Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct) => Task.CompletedTask;
    Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct) => Task.CompletedTask;
    Task OnRowFailedAsync(BulkRowFailedEvent e, CancellationToken ct) => Task.CompletedTask;
}
