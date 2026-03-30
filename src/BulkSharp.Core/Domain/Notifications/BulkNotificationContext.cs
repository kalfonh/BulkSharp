using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Notifications;

/// <summary>
/// Context passed to notification channels when dispatching a notification.
/// Contains the triggering event, the matched recipient, and the full operation record.
/// </summary>
public sealed class BulkNotificationContext
{
    /// <summary>The event that triggered this notification.</summary>
    public required BulkOperationEvent Event { get; init; }

    /// <summary>The recipient being notified.</summary>
    public required NotificationRecipient Recipient { get; init; }

    /// <summary>The full operation record at the time of notification.</summary>
    public required BulkOperation Operation { get; init; }
}
