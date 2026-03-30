namespace BulkSharp.Core.Domain.Notifications;

/// <summary>
/// Flags indicating which operation lifecycle events should trigger a notification.
/// Combine flags to notify on multiple events.
/// </summary>
[Flags]
public enum NotificationTrigger
{
    None = 0,
    OnCompletion = 1,
    OnFailure = 2,
    OnCompletionWithErrors = 4,
    OnCancelled = 8,
    OnStatusChange = 16,
    OnTerminal = OnCompletion | OnFailure | OnCompletionWithErrors | OnCancelled,
    All = OnTerminal | OnStatusChange
}
