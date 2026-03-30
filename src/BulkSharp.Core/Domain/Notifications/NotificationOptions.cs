namespace BulkSharp.Core.Domain.Notifications;

/// <summary>
/// Per-operation notification preferences. Passed at operation creation time
/// and stored alongside the operation. Null or empty recipients = no notifications.
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>
    /// The list of notification recipients for this operation.
    /// </summary>
    public List<NotificationRecipient> Recipients { get; set; } = [];
}
