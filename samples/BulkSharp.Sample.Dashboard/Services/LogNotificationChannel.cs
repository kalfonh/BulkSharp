using BulkSharp.Core.Abstractions.Notifications;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Notifications;

namespace BulkSharp.Sample.Dashboard.Services;

/// <summary>
/// Sample notification channel that logs notifications.
/// Demonstrates how to implement IBulkNotificationChannel for the dashboard sample.
/// Replace with a real channel (email, Slack, webhook) in production.
/// </summary>
public sealed class LogNotificationChannel(ILogger<LogNotificationChannel> logger) : IBulkNotificationChannel
{
    public string ChannelName => "email";

    public Task SendAsync(BulkNotificationContext context, CancellationToken cancellationToken = default)
    {
        var statusSummary = context.Event switch
        {
            BulkOperationCompletedEvent c => $"{c.SuccessfulRows}/{c.TotalRows} rows succeeded in {c.Duration:g}",
            BulkOperationFailedEvent f => $"Error: {f.ErrorMessage}",
            _ => context.Operation.Status.ToString()
        };

        logger.LogInformation(
            "[NOTIFICATION] Channel={Channel} Target={Target} Operation={OperationName} Status={Status} — {Summary}",
            context.Recipient.Channel,
            context.Recipient.Target,
            context.Operation.OperationName,
            context.Operation.Status,
            statusSummary);

        return Task.CompletedTask;
    }
}
