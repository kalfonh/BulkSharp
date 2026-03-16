namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Dispatching {EventType} for operation {OperationId} to {HandlerCount} handlers")]
    public static partial void DispatchingEvent(this ILogger logger, string eventType, Guid operationId, int handlerCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event handler {HandlerType} failed for {EventType} on operation {OperationId}")]
    public static partial void EventHandlerFailed(this ILogger logger, Exception ex, string handlerType, string eventType, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to dispatch {EventType} event for operation {OperationId}")]
    public static partial void EventDispatchFailed(this ILogger logger, Exception ex, Guid operationId, string eventType);
}
