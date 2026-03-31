using System.Net;
using System.Net.Mail;
using BulkSharp.Core.Abstractions.Notifications;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Notifications;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Sample.Production.Configuration;
using Microsoft.Extensions.Options;

namespace BulkSharp.Sample.Production.Services;

/// <summary>
/// Sample email notification channel using SmtpClient.
/// For production use, consider MailKit or a transactional email service (SendGrid, SES, etc.).
/// </summary>
public sealed class SmtpEmailNotificationChannel(
    IOptions<SmtpSettings> settings,
    ILogger<SmtpEmailNotificationChannel> logger) : IBulkNotificationChannel
{
    public string ChannelName => "email";

    public async Task SendAsync(BulkNotificationContext context, CancellationToken cancellationToken = default)
    {
        var smtp = settings.Value;
        var subject = BuildSubject(context);
        var body = BuildBody(context);

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(context.Recipient.Target);

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseSsl,
            Credentials = !string.IsNullOrEmpty(smtp.Username)
                ? new NetworkCredential(smtp.Username, smtp.Password)
                : null
        };

        logger.LogInformation(
            "Sending email notification to {Recipient} for operation {OperationId}",
            context.Recipient.Target, context.Operation.Id);

        await client.SendMailAsync(message, cancellationToken);
    }

    private static string BuildSubject(BulkNotificationContext context)
    {
        var status = context.Operation.Status switch
        {
            BulkOperationStatus.Completed => "Completed",
            BulkOperationStatus.CompletedWithErrors => "Completed with Errors",
            BulkOperationStatus.Failed => "Failed",
            BulkOperationStatus.Cancelled => "Cancelled",
            _ => context.Operation.Status.ToString()
        };
        return $"[BulkSharp] {context.Operation.OperationName} \u2014 {status}";
    }

    private static string BuildBody(BulkNotificationContext context)
    {
        var op = context.Operation;
        var lines = new List<string>
        {
            $"Operation: {op.OperationName}",
            $"Status: {op.Status}",
            $"File: {op.FileName}",
            $"Created by: {op.CreatedBy}",
            $"Started: {op.StartedAt:u}",
            $"Completed: {op.CompletedAt:u}",
            ""
        };

        if (context.Event is BulkOperationCompletedEvent completed)
        {
            lines.Add($"Total rows: {completed.TotalRows}");
            lines.Add($"Successful: {completed.SuccessfulRows}");
            lines.Add($"Failed: {completed.FailedRows}");
            lines.Add($"Duration: {completed.Duration:g}");
        }
        else if (context.Event is BulkOperationFailedEvent failed)
        {
            lines.Add($"Error: {failed.ErrorMessage}");
            lines.Add($"Processed rows: {failed.ProcessedRows} / {failed.TotalRows}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
