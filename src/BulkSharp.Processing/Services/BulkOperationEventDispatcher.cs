using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class BulkOperationEventDispatcher : IBulkOperationEventDispatcher
{
    private readonly IEnumerable<IBulkOperationEventHandler> _handlers;
    private readonly ILogger<BulkOperationEventDispatcher> _logger;

    public BulkOperationEventDispatcher(
        IEnumerable<IBulkOperationEventHandler> handlers,
        ILogger<BulkOperationEventDispatcher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task DispatchAsync(BulkOperationEvent e, CancellationToken ct = default)
    {
        var handlerList = _handlers.ToList();
        if (handlerList.Count == 0)
            return;

        var eventType = e.GetType().Name;
        _logger.DispatchingEvent(eventType, e.OperationId, handlerList.Count);

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
            _logger.EventHandlerFailed(ex, handler.GetType().Name, eventType, e.OperationId);
        }
    }
}
