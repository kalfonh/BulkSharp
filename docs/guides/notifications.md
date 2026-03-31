---
title: Per-Operation Notifications
---

# Per-Operation Notifications

BulkSharp supports per-operation notification preferences. When creating a bulk operation, callers can attach a `NotificationOptions` object specifying who should be notified, through which channel, and on which lifecycle events. If no preferences are provided, no notifications are sent — zero overhead.

## Quick Start

### 1. Implement a notification channel

```csharp
public class EmailNotificationChannel(IEmailService emailService)
    : IBulkNotificationChannel
{
    public string ChannelName => "email";

    public async Task SendAsync(BulkNotificationContext context, CancellationToken ct)
    {
        var subject = $"[BulkSharp] {context.Operation.OperationName} — {context.Operation.Status}";
        await emailService.SendAsync(context.Recipient.Target, subject, BuildBody(context), ct);
    }

    private static string BuildBody(BulkNotificationContext context) =>
        context.Event switch
        {
            BulkOperationCompletedEvent c =>
                $"Completed: {c.SuccessfulRows}/{c.TotalRows} rows in {c.Duration:g}",
            BulkOperationFailedEvent f =>
                $"Failed: {f.ErrorMessage}",
            _ => $"Status: {context.Operation.Status}"
        };
}
```

### 2. Register the channel

```csharp
services.AddBulkSharp(builder => builder
    .AddNotificationChannel<EmailNotificationChannel>());
```

Channels are also auto-discovered from scanned assemblies (same as event handlers). If your channel is in a scanned assembly, you can skip manual registration.

### 3. Pass notification preferences at creation time

```csharp
var notifications = new NotificationOptions
{
    Recipients =
    [
        new("email", "alice@example.com")
        {
            Triggers = NotificationTrigger.OnCompletion | NotificationTrigger.OnFailure
        },
        new("email", "ops@example.com")
        {
            Triggers = NotificationTrigger.OnFailure
        }
    ]
};

var operationId = await operationService.CreateBulkOperationAsync(
    "import-users", fileStream, "users.csv", metadata, "alice",
    notifications, cancellationToken);
```

## Notification Triggers

Triggers are flags — combine them with `|` to notify on multiple events.

| Flag | Value | Fires when |
|------|-------|------------|
| `None` | 0 | Never |
| `OnCompletion` | 1 | Operation completed successfully (all rows passed) |
| `OnFailure` | 2 | Operation failed |
| `OnCompletionWithErrors` | 4 | Operation completed but some rows failed |
| `OnCancelled` | 8 | Operation was cancelled |
| `OnStatusChange` | 16 | Any status transition (Validating, Running, etc.) |
| `OnTerminal` | 15 | Any terminal state (Completion + Failure + CompletedWithErrors + Cancelled) |
| `All` | 31 | Every lifecycle event |

Each recipient has its own trigger configuration. This allows different notification rules per recipient within the same operation:

```csharp
new NotificationOptions
{
    Recipients =
    [
        // Developer gets notified on everything
        new("email", "dev@example.com") { Triggers = NotificationTrigger.All },
        // Ops only cares about failures
        new("email", "ops@example.com") { Triggers = NotificationTrigger.OnFailure },
        // Slack channel gets terminal events
        new("slack", "#imports") { Triggers = NotificationTrigger.OnTerminal }
    ]
}
```

## How It Works

Notifications are built on top of the existing [Event Hooks](configuration.md#event-hooks) system:

1. A built-in `NotificationEventHandler` (an `IBulkOperationEventHandler`) is registered automatically
2. When an event fires, it reads `NotificationOptionsJson` from the operation record
3. Filters recipients whose `Triggers` match the current event
4. Routes each matching recipient to the registered `IBulkNotificationChannel` by `ChannelName`

If no channels are registered or no operation has notification preferences, the handler short-circuits immediately.

```
Event Pipeline
    |
    +-- Your custom IBulkOperationEventHandler (global, all operations)
    +-- NotificationEventHandler (built-in)
            |
            | reads NotificationOptionsJson from operation
            | filters by trigger flags
            |
            +-- IBulkNotificationChannel "email"  -> SendAsync()
            +-- IBulkNotificationChannel "slack"   -> SendAsync()
            +-- IBulkNotificationChannel "webhook" -> SendAsync()
```

## Implementing IBulkNotificationChannel

```csharp
[BulkExtensionPoint]
public interface IBulkNotificationChannel
{
    string ChannelName { get; }
    Task SendAsync(BulkNotificationContext context, CancellationToken ct);
}
```

The `BulkNotificationContext` provides:

| Property | Type | Description |
|----------|------|-------------|
| `Event` | `BulkOperationEvent` | The triggering event (cast to specific type for details) |
| `Recipient` | `NotificationRecipient` | The matched recipient (channel, target, triggers) |
| `Operation` | `BulkOperation` | Full operation record at time of notification |

**Error handling:** Channel exceptions are caught, logged, and swallowed. A failing channel never blocks processing or prevents other channels from firing.

**Unmatched channels:** If a recipient specifies a channel name that has no registered `IBulkNotificationChannel`, a warning is logged and the recipient is skipped.

## API Endpoints

The Dashboard API endpoint (`POST /api/bulks`) accepts an optional `notifications` form field containing JSON:

```json
{
  "recipients": [
    {
      "channel": "email",
      "target": "user@example.com",
      "triggers": 3
    }
  ]
}
```

The `triggers` value is the integer representation of the `NotificationTrigger` flags (e.g., `3` = `OnCompletion | OnFailure`).

## When to Use Notifications vs Event Handlers

| Use case | Mechanism |
|----------|-----------|
| Global concern (logging, metrics, audit) | `IBulkOperationEventHandler` |
| Per-operation, per-recipient alerts | `IBulkNotificationChannel` + `NotificationOptions` |
| "Always email ops on failure" | Implement the logic inside your `IBulkNotificationChannel` |
| "This specific import should also notify Alice" | Pass `NotificationOptions` at creation time |

## See Also

- [Configuration — Event Hooks](configuration.md#event-hooks)
- [Custom Providers](custom-providers.md)
- [Assembly Scanning](assembly-scanning.md)
