# Async Steps

For steps that initiate work and wait for external completion, use `IAsyncBulkStep`. This supports two completion modes: **Polling** and **Signal**.

## Polling Mode

BulkSharp calls `CheckCompletionAsync` at regular intervals until it returns `true` or the step times out.

```csharp
public class ProvisionVmStep : IAsyncBulkStep<Metadata, Row>
{
    public string Name => "Provision VM";
    public int MaxRetries => 2;
    public StepCompletionMode CompletionMode => StepCompletionMode.Polling;
    public TimeSpan PollInterval => TimeSpan.FromSeconds(10);
    public TimeSpan Timeout => TimeSpan.FromMinutes(15);

    public Task ExecuteAsync(Row row, Metadata meta, CancellationToken ct)
    {
        // Kick off VM provisioning (returns immediately)
    }

    public Task<bool> CheckCompletionAsync(Row row, Metadata meta, CancellationToken ct)
    {
        // Check if the VM is ready. Return true when done.
    }

    public string GetSignalKey(Row row, Metadata meta) => row.RowId!;
}
```

Flow:
1. `ExecuteAsync` runs once to initiate the work
2. `CheckCompletionAsync` is called every `PollInterval`
3. When it returns `true`, the step completes
4. If `Timeout` is exceeded, the step fails with `TimedOut`

## Signal Mode

BulkSharp waits for an external system to signal completion via the REST API.

```csharp
public class ApprovalStep : IAsyncBulkStep<Metadata, Row>
{
    public string Name => "Manager Approval";
    public int MaxRetries => 0;
    public StepCompletionMode CompletionMode => StepCompletionMode.Signal;
    public TimeSpan PollInterval => TimeSpan.Zero;  // Not used in signal mode
    public TimeSpan Timeout => TimeSpan.FromHours(24);

    public Task ExecuteAsync(Row row, Metadata meta, CancellationToken ct)
    {
        // Send approval request (email, Slack, etc.)
    }

    public Task<bool> CheckCompletionAsync(Row row, Metadata meta, CancellationToken ct)
        => Task.FromResult(false);  // Not used in signal mode

    public string GetSignalKey(Row row, Metadata meta) => $"approval-{row.RowId}";
}
```

### Signaling Completion

External systems signal completion via the dashboard REST API:

```bash
# Signal success
POST /api/bulks/{operationId}/signal/approval-{rowId}

# Signal failure with error message
POST /api/bulks/{operationId}/signal/approval-{rowId}/fail
Content-Type: application/json
"Approval denied by manager"
```

The `GetSignalKey` return value is the `{key}` in the URL. BulkSharp scopes it internally to the operation.

### Signal Key Design

BulkSharp automatically scopes signal keys per operation and per row (`{operationId}:{userKey}:{rowNumber}`). This means:
- Two rows returning the same user key won't collide — the row number differentiates them
- Two operations processing the same data won't collide — the operation ID differentiates them

Choose keys meaningful to the external system:

```csharp
// Good: domain-meaningful, external system knows the order ID
public string GetSignalKey(Row row, Metadata meta) => $"order-{row.OrderId}";

// Good: simple, relies on framework row-scoping
public string GetSignalKey(Row row, Metadata meta) => "approval";

// Good: using RowId
public string GetSignalKey(Row row, Metadata meta) => row.RowId!;
```

When multiple rows share the same user key, signaling via the REST API completes the first waiting row (FIFO). See [Signal Service](signal-service.md) for details.

## Orphaned Step Recovery

If the application restarts while signal-mode steps are waiting, those rows become orphaned (stuck in `WaitingForCompletion`). Enable recovery to automatically transition them to `Failed` after restart:

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.EnableOrphanedStepRecovery = true));
```

## Combining with Parallel Processing

Signal-mode steps pair well with `MaxRowConcurrency > 1`. Multiple rows can wait for signals concurrently instead of sequentially:

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts =>
    {
        opts.MaxRowConcurrency = 10;  // 10 rows waiting for signals concurrently
    }));
```

Without parallel processing, each row would wait for its signal before the next row starts.
