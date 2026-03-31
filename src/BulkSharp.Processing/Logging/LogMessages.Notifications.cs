namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Dispatching notification for operation {OperationId} to {RecipientCount} recipients")]
    public static partial void DispatchingNotification(this ILogger logger, Guid operationId, int recipientCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification channel '{ChannelName}' not found for operation {OperationId}")]
    public static partial void NotificationChannelNotFound(this ILogger logger, string channelName, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification channel '{ChannelName}' failed for operation {OperationId}")]
    public static partial void NotificationChannelFailed(this ILogger logger, Exception ex, string channelName, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize notification options for operation {OperationId}")]
    public static partial void NotificationOptionsDeserializationFailed(this ILogger logger, Exception ex, Guid operationId);
}
