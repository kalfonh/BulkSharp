using BulkSharp.Core.Attributes;
using BulkSharp.Core.Domain.Notifications;

namespace BulkSharp.Core.Abstractions.Notifications;

/// <summary>
/// Plugin interface for notification delivery channels (email, Slack, webhook, etc.).
/// Implementations are auto-discovered from scanned assemblies or registered
/// manually via <c>builder.AddNotificationChannel&lt;T&gt;()</c>.
/// </summary>
[BulkExtensionPoint]
public interface IBulkNotificationChannel
{
    /// <summary>
    /// The channel identifier. Must match <see cref="NotificationRecipient.Channel"/>
    /// for routing (e.g., "email", "slack", "webhook").
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Sends a notification to the specified recipient.
    /// Exceptions are caught by the dispatcher and logged — they never block processing.
    /// </summary>
    Task SendAsync(BulkNotificationContext context, CancellationToken cancellationToken = default);
}
