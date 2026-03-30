namespace BulkSharp.Core.Domain.Notifications;

/// <summary>
/// A single notification recipient with a target channel and trigger configuration.
/// </summary>
public sealed class NotificationRecipient
{
    /// <summary>
    /// The notification channel name (e.g., "email", "slack", "webhook").
    /// Must match <c>IBulkNotificationChannel.ChannelName</c>.
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// The channel-specific target (e.g., email address, Slack channel ID, webhook URL).
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Which lifecycle events trigger a notification for this recipient.
    /// Defaults to <see cref="NotificationTrigger.OnFailure"/>.
    /// </summary>
    public NotificationTrigger Triggers { get; set; } = NotificationTrigger.OnFailure;

    public NotificationRecipient() { }

    public NotificationRecipient(string channel, string target)
    {
        Channel = channel;
        Target = target;
    }
}
