# Signal-Based Step Completion

The `IBulkStepSignalService` provides a programmatic API for signaling completion of signal-mode async steps. This is the mechanism that bridges external systems (webhooks, message queues, manual approvals) to the BulkSharp processing pipeline.

For async step basics and polling mode, see [Async Steps](async-steps.md).

## IBulkStepSignalService API

```csharp
public interface IBulkStepSignalService
{
    bool TrySignal(string signalKey);
    bool TrySignalFailure(string signalKey, string errorMessage);
}
```

Both methods return `true` if a waiter was found and signaled, `false` if no step is waiting on that key.

- **TrySignal**: Marks the step as completed successfully. The pipeline proceeds to the next step.
- **TrySignalFailure**: Marks the step as failed with the provided error message. The error is recorded and the row is marked as failed.

The service is registered as a singleton. Inject it anywhere you need to signal step completion.

## How Signal Keys Work

Signal keys are scoped to prevent collisions across operations and rows. When your step returns a key from `GetSignalKey`, BulkSharp builds the internal key as:

```
Internal key = "{operationId}:{userKey}:{rowNumber}"
```

The row number is appended automatically, so even if two rows return the same user key, their internal keys are unique. This means `GetSignalKey` does **not** need to include the row identifier — though including it is still recommended for clarity in logs and external systems.

Your step defines the user-facing portion:

```csharp
public string GetSignalKey(Row row, Metadata meta) => $"order-{row.OrderId}";
```

When signaling via the REST API, pass only the user portion — the endpoint matches by prefix:

```
POST /api/bulks/{operationId}/signal/order-{orderId}
```

If multiple rows share the same user key (e.g., all rows use `"approval"`), the signal endpoint completes the **first waiting row** (FIFO). Each subsequent signal call completes the next waiting row.

When signaling via the programmatic API, you need the **full scoped key** (including row number):

```csharp
signalService.TrySignal($"{operationId}:order-{orderId}:{rowNumber}");
```

Or query the DB for the signal key:

```csharp
var status = await statusRepo.GetBySignalKeyAsync(fullScopedKey, ct);
signalService.TrySignal(status.SignalKey);
```

## Signal Flow

1. Step executor calls `ExecuteAsync` on the async step (kicks off external work)
2. `SignalCompletionHandler` registers a `TaskCompletionSource` in the in-process signal registry using the scoped key
3. The row enters `WaitingForCompletion` state and is persisted to the DB
4. An external system calls `TrySignal` (in-process) or the REST endpoint
5. The `TaskCompletionSource` completes, the step executor resumes
6. If `Timeout` elapses before a signal arrives, the step fails with `BulkStepTimeoutException`

## Cross-Process Signals (API + Worker Architecture)

In a [split API + Worker](../getting-started/api-worker.md) deployment, the API process and Worker process don't share memory. The in-process `TrySignal()` on the API side cannot reach the Worker's `TaskCompletionSource`.

BulkSharp handles this automatically:

1. The signal REST endpoint first tries the in-process signal (same-process case)
2. If no in-process waiter is found (cross-process case), it writes the completion state directly to the DB (`BulkRowStepStatus.State = Completed`)
3. The Worker's `SignalCompletionHandler` polls the DB every 2 seconds alongside the in-process waiter
4. When the DB state changes to `Completed` or `Failed`, the Worker picks it up and resumes the step

This means **signal endpoints work identically** whether the API and Worker are in the same process or separate processes. No configuration needed.

## Integration Patterns

### Webhook Callback

Register an endpoint that receives callbacks from an external system. Use the dashboard's built-in signal endpoint — it handles scoping and cross-process signals:

```csharp
app.MapPost("/webhooks/payment-processor", async (
    [FromBody] PaymentWebhook webhook,
    HttpClient http) =>
{
    // Forward to BulkSharp signal endpoint (handles scoping + cross-process)
    var signalUrl = $"/api/bulks/{webhook.BulkOperationId}/signal/payment-{webhook.OrderId}";

    if (webhook.Status == "completed")
    {
        await http.PostAsync(signalUrl, null);
    }
    else
    {
        await http.PostAsJsonAsync($"{signalUrl}/fail",
            new { ErrorMessage = $"Payment failed: {webhook.Reason}" });
    }

    return Results.Ok();
});
```

The async step that initiates the payment would pass the callback URL with the operation ID and signal key embedded:

```csharp
public class PaymentStep : IAsyncBulkStep<OrderMetadata, OrderRow>
{
    public string Name => "Process Payment";
    public StepCompletionMode CompletionMode => StepCompletionMode.Signal;
    public TimeSpan Timeout => TimeSpan.FromMinutes(30);
    public TimeSpan PollInterval => TimeSpan.Zero;
    public int MaxRetries => 1;

    private readonly IPaymentClient _paymentClient;

    public PaymentStep(IPaymentClient paymentClient)
        => _paymentClient = paymentClient;

    public async Task ExecuteAsync(OrderRow row, OrderMetadata meta, CancellationToken ct)
    {
        await _paymentClient.InitiatePaymentAsync(new PaymentRequest
        {
            OrderId = row.OrderId,
            Amount = row.Amount,
            CallbackUrl = $"{meta.BaseUrl}/webhooks/payment-processor",
            CallbackMetadata = new { BulkOperationId = meta.OperationId, OrderId = row.OrderId }
        }, ct);
    }

    public Task<bool> CheckCompletionAsync(OrderRow row, OrderMetadata meta, CancellationToken ct)
        => Task.FromResult(false);

    public string GetSignalKey(OrderRow row, OrderMetadata meta) => $"payment-{row.OrderId}";
}
```

### Message Queue Consumer (Same-Process Worker)

When the consumer runs in the same process as the Worker, use `IBulkStepSignalService` directly. You need the full scoped key — query the DB for waiting statuses:

```csharp
public class ApprovalQueueConsumer : BackgroundService
{
    private readonly IBulkStepSignalService _signalService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageQueue _queue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var message in _queue.ConsumeAsync("approval-responses", ct))
        {
            using var scope = _serviceProvider.CreateScope();
            var rowRecordRepo = scope.ServiceProvider.GetRequiredService<IBulkRowRecordRepository>();

            // Find the waiting step by querying the DB
            var waiting = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
            {
                OperationId = message.OperationId,
                State = RowRecordState.WaitingForCompletion
            }, ct);

            var record = waiting.Items.FirstOrDefault(r =>
                r.SignalKey?.Contains($"approval-{message.TicketId}:") == true);

            if (record?.SignalKey != null)
            {
                if (message.Approved)
                    _signalService.TrySignal(record.SignalKey);
                else
                    _signalService.TrySignalFailure(record.SignalKey, message.DenialReason);
            }

            await _queue.AcknowledgeAsync(message, ct);
        }
    }
}
```

### Message Queue Consumer (Separate Process)

When the consumer runs in a different process than the Worker, use the REST signal endpoint instead — it handles cross-process DB-backed signaling automatically:

```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    await foreach (var message in _queue.ConsumeAsync("approval-responses", ct))
    {
        var signalUrl = $"{_bulkSharpApiUrl}/api/bulks/{message.OperationId}/signal/approval-{message.TicketId}";

        if (message.Approved)
            await _http.PostAsync(signalUrl, null, ct);
        else
            await _http.PostAsJsonAsync($"{signalUrl}/fail",
                new { ErrorMessage = message.DenialReason }, ct);

        await _queue.AcknowledgeAsync(message, ct);
    }
}
```
```

### REST API (Dashboard Endpoints)

The dashboard exposes built-in signal endpoints - no custom code needed:

```bash
# Signal success
curl -X POST http://localhost:5000/api/bulks/{operationId}/signal/{key}

# Signal failure
curl -X POST http://localhost:5000/api/bulks/{operationId}/signal/{key}/fail \
  -H "Content-Type: application/json" \
  -d '{"errorMessage": "Approval denied"}'
```

These endpoints look up the step status by signal key, verify it is still waiting, then call `TrySignal` or `TrySignalFailure`.

## Orphaned Step Recovery

Signal-mode steps hold an in-process `TaskCompletionSource`. If the application restarts while steps are waiting, those waiters are lost and the rows become permanently stuck in `WaitingForCompletion`.

Enable orphaned step recovery to handle this:

```csharp
builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts => opts.EnableOrphanedStepRecovery = true));
```

On startup, `OrphanedStepRecoveryService` (a hosted service) runs automatically:

1. Queries all `Running` operations
2. Finds rows in `WaitingForCompletion` state older than 5 minutes
3. Transitions them to `Failed` with the message: "Recovery: step was waiting for signal when application restarted"
4. Marks the parent operation as failed

It also detects stuck `Running` operations with no waiting rows (e.g., the app crashed mid-processing) and marks them as failed.

If the database is not yet available (first startup before migrations), the recovery service logs a warning and exits gracefully without throwing.

**When to enable**: Production deployments with signal-mode steps where restarts are possible. Disable for development or when using only polling-mode steps.

## Complete Example

A shipment processing operation that waits for carrier confirmation:

```csharp
[BulkOperation("shipment-tracking")]
public class ShipmentOperation : IBulkPipelineOperation<ShipmentMetadata, ShipmentRow>
{
    public Task ValidateMetadataAsync(ShipmentMetadata meta, CancellationToken ct)
        => Task.CompletedTask;

    public Task ValidateRowAsync(ShipmentRow row, ShipmentMetadata meta, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(row.TrackingNumber))
            throw new ValidationException("Tracking number is required");
        return Task.CompletedTask;
    }

    public IEnumerable<IBulkStep<ShipmentMetadata, ShipmentRow>> GetSteps()
    {
        yield return new SubmitToCarrierStep();
        yield return new WaitForPickupStep();   // Signal-mode async step
        yield return new UpdateInventoryStep();
    }
}

public class WaitForPickupStep : IAsyncBulkStep<ShipmentMetadata, ShipmentRow>
{
    public string Name => "Wait for Pickup";
    public int MaxRetries => 0;
    public StepCompletionMode CompletionMode => StepCompletionMode.Signal;
    public TimeSpan PollInterval => TimeSpan.Zero;
    public TimeSpan Timeout => TimeSpan.FromHours(48);

    public Task ExecuteAsync(ShipmentRow row, ShipmentMetadata meta, CancellationToken ct)
    {
        // Register webhook with carrier - carrier will call back at /webhooks/carrier
        return Task.CompletedTask;
    }

    public Task<bool> CheckCompletionAsync(ShipmentRow row, ShipmentMetadata meta, CancellationToken ct)
        => Task.FromResult(false);

    public string GetSignalKey(ShipmentRow row, ShipmentMetadata meta)
        => $"pickup-{row.TrackingNumber}";
}
```

Webhook endpoint (using built-in signal API for cross-process support):

```csharp
app.MapPost("/webhooks/carrier", async (
    [FromBody] CarrierEvent evt,
    HttpClient http) =>
{
    // Use the built-in signal endpoint — works across processes
    await http.PostAsync(
        $"/api/bulks/{evt.OperationId}/signal/pickup-{evt.TrackingNumber}", null);
    return Results.Ok();
});
```

Startup configuration:

```csharp
builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts =>
    {
        opts.MaxRowConcurrency = 10;
        opts.EnableOrphanedStepRecovery = true;
    })
    .AddOperationsFromAssemblyOf<ShipmentOperation>()
);
```
