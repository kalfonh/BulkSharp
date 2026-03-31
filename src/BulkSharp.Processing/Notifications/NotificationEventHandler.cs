using System.Text.Json;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Abstractions.Notifications;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Notifications;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Notifications;

internal sealed class NotificationEventHandler(
    IEnumerable<IBulkNotificationChannel> channels,
    IBulkOperationRepository operationRepository,
    ILogger<NotificationEventHandler> logger) : IBulkOperationEventHandler
{
    private readonly Dictionary<string, IBulkNotificationChannel> _channelMap =
        channels.ToDictionary(c => c.ChannelName, StringComparer.OrdinalIgnoreCase);

    public Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        var trigger = e.Status == BulkOperationStatus.CompletedWithErrors
            ? NotificationTrigger.OnCompletionWithErrors
            : NotificationTrigger.OnCompletion;
        return DispatchNotificationsAsync(e, trigger, ct);
    }

    public Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct) =>
        DispatchNotificationsAsync(e, NotificationTrigger.OnFailure, ct);

    public Task OnStatusChangedAsync(BulkOperationStatusChangedEvent e, CancellationToken ct) =>
        DispatchNotificationsAsync(e, NotificationTrigger.OnStatusChange, ct);

    private async Task DispatchNotificationsAsync(BulkOperationEvent e, NotificationTrigger trigger, CancellationToken ct)
    {
        if (_channelMap.Count == 0)
            return;

        var operation = await operationRepository.GetByIdAsync(e.OperationId, ct).ConfigureAwait(false);
        if (operation == null || string.IsNullOrEmpty(operation.NotificationOptionsJson))
            return;

        NotificationOptions? options;
        try
        {
            options = JsonSerializer.Deserialize<NotificationOptions>(operation.NotificationOptionsJson);
        }
        catch (JsonException ex)
        {
            logger.NotificationOptionsDeserializationFailed(ex, e.OperationId);
            return;
        }

        if (options?.Recipients == null || options.Recipients.Count == 0)
            return;

        var matchingRecipients = options.Recipients
            .Where(r => r.Triggers.HasFlag(trigger))
            .ToList();

        if (matchingRecipients.Count == 0)
            return;

        logger.DispatchingNotification(e.OperationId, matchingRecipients.Count);

        foreach (var recipient in matchingRecipients)
        {
            if (!_channelMap.TryGetValue(recipient.Channel, out var channel))
            {
                logger.NotificationChannelNotFound(recipient.Channel, e.OperationId);
                continue;
            }

            try
            {
                var context = new BulkNotificationContext
                {
                    Event = e,
                    Recipient = recipient,
                    Operation = operation
                };
                await channel.SendAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.NotificationChannelFailed(ex, recipient.Channel, e.OperationId);
            }
        }
    }
}
