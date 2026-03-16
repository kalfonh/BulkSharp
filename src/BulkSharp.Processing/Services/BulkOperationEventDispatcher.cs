using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class BulkOperationEventDispatcher(
    IEnumerable<IBulkOperationEventHandler> handlers,
    ILogger<BulkOperationEventDispatcher> logger) : IBulkOperationEventDispatcher
{
    public async Task DispatchAsync(BulkOperationEvent e, CancellationToken ct = default)
    {
        var handlerList = handlers.ToList();
        if (handlerList.Count == 0)
            return;

        var eventType = e.GetType().Name;
        logger.DispatchingEvent(eventType, e.OperationId, handlerList.Count);

        var tasks = handlerList.Select(handler => InvokeHandlerAsync(handler, e, eventType, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task InvokeHandlerAsync(
        IBulkOperationEventHandler handler,
        BulkOperationEvent e,
        string eventType,
        CancellationToken ct)
    {
        try
        {
            var task = e switch
            {
                BulkOperationCreatedEvent created => handler.OnOperationCreatedAsync(created, ct),
                BulkOperationStatusChangedEvent changed => handler.OnStatusChangedAsync(changed, ct),
                BulkOperationCompletedEvent completed => handler.OnOperationCompletedAsync(completed, ct),
                BulkOperationFailedEvent failed => handler.OnOperationFailedAsync(failed, ct),
                BulkRowFailedEvent rowFailed => handler.OnRowFailedAsync(rowFailed, ct),
                _ => Task.CompletedTask
            };

            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.EventHandlerFailed(ex, handler.GetType().Name, eventType, e.OperationId);
        }
    }
}
