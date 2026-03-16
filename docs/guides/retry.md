# Retry Guide

BulkSharp supports operation-level retry for failed rows. When an operation completes with errors, you can retry the failed rows without re-uploading the file or re-processing successful rows.

---

## Enabling Retryability

Two conditions must be met for an operation to support retry:

### 1. Mark the Operation as Retryable

Use the `IsRetryable` property on the `[BulkOperation]` attribute:

```csharp
[BulkOperation("import-users", IsRetryable = true, TrackRowData = true)]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow> { ... }
```

Or override the `IsRetryable` property on the operation interface:

```csharp
[BulkOperation("import-users", TrackRowData = true)]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow>
{
    public bool IsRetryable => true;
    // ...
}
```

Either approach works. The attribute property and the interface property are checked independently -- if either is `true`, the operation is retryable.

### 2. Enable Row Data Tracking

Retry requires serialized row data to re-process rows without re-reading the file:

```csharp
[BulkOperation("import-users", IsRetryable = true, TrackRowData = true)]
```

If `TrackRowData` is not enabled, retry will return an ineligible result with the reason `"TrackRowData must be enabled for retry"`.

---

## Step-Level Retry Control

Individual steps in a pipeline operation can opt out of operation-level retry using `AllowOperationRetry`:

### Via Attribute

```csharp
[BulkStep("Send Notification", Order = 3, AllowOperationRetry = false)]
public async Task SendNotificationAsync(Row row, Metadata meta, CancellationToken ct)
{
    // If this step fails, the row will be skipped during retry
}
```

### Via Class

```csharp
public class NotificationStep : IBulkStep<Metadata, Row>
{
    public string Name => "Send Notification";
    public int MaxRetries => 1;
    public bool AllowOperationRetry => false;  // Default is true

    public Task ExecuteAsync(Row row, Metadata meta, CancellationToken ct) { ... }
}
```

When a row failed at a step with `AllowOperationRetry = false`, the retry service skips that row and includes the reason in `RetrySubmission.SkippedReasons`.

---

## API

### Check Eligibility

```csharp
var eligibility = await service.CanRetryAsync(operationId);

if (eligibility.IsEligible)
{
    Console.WriteLine("Operation can be retried");
}
else
{
    Console.WriteLine($"Cannot retry: {eligibility.Reason}");
}
```

Eligibility checks:
- Operation status must be `CompletedWithErrors`
- Operation must be retryable (`IsRetryable = true`)
- `TrackRowData` must be enabled
- `RetryCount` must be below `MaxRetryAttempts`
- At least one failed row with `StepIndex >= 0` must exist (validation failures are excluded)

### Retry All Failed Rows

```csharp
var result = await service.RetryFailedRowsAsync(operationId, new RetryRequest());

Console.WriteLine($"Submitted: {result.RowsSubmitted}");
Console.WriteLine($"Skipped: {result.RowsSkipped}");
if (result.SkippedReasons != null)
{
    foreach (var reason in result.SkippedReasons)
        Console.WriteLine($"  - {reason}");
}
```

### Retry Specific Rows

```csharp
var result = await service.RetryFailedRowsAsync(operationId, new RetryRequest
{
    RowNumbers = [42, 87, 103]
});
```

### Retry a Single Row

```csharp
var result = await service.RetryRowAsync(operationId, rowNumber: 42);
```

### Query Retry History

```csharp
var history = await service.QueryRetryHistoryAsync(new BulkRowRetryHistoryQuery
{
    OperationId = operationId,
    RowNumber = 42,    // optional
    Page = 1,
    PageSize = 100
});

foreach (var entry in history.Items)
{
    Console.WriteLine($"Row {entry.RowNumber} attempt {entry.Attempt}: " +
                      $"[{entry.ErrorType}] {entry.ErrorMessage}");
}
```

---

## Configuration

### MaxRetryAttempts

Controls the maximum number of retry attempts per operation:

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.MaxRetryAttempts = 5));  // Default: 10
```

Set to `0` to disable the limit (unlimited retries).

---

## Dashboard Endpoints

The dashboard exposes REST endpoints for retry operations:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/bulks/{id}/retry/eligibility` | Check if an operation can be retried |
| `POST` | `/api/bulks/{id}/retry` | Retry all failed rows |
| `POST` | `/api/bulks/{id}/retry/rows` | Retry specific rows (body: `{ "rowNumbers": [1, 2, 3] }`) |
| `GET` | `/api/bulks/{id}/retry/history` | Query retry history (supports `rowNumber`, `page`, `pageSize`) |

---

## What Gets Retried (and What Does Not)

**Retried:**
- Rows that failed during step execution (`StepIndex >= 0`)
- Rows that timed out during async steps
- Rows where the step's `AllowOperationRetry` is `true` (default)

**Not retried:**
- Rows that failed validation (`StepIndex = -1`, `BulkErrorType.Validation`)
- Rows where the failing step has `AllowOperationRetry = false`

---

## Retry Flow Summary

1. `RetryFailedRowsAsync` is called
2. Eligibility is verified
3. Failed rows are queried (`ErrorsOnly = true`, `StepIndex >= 0`)
4. Step-level `AllowOperationRetry` is checked per row
5. Error snapshots are saved to `BulkRowRetryHistory`
6. Row records are reset via `ResetForRetry(stepIndex)`: state becomes `Pending`, `RetryAttempt` increments
7. Operation transitions to `Retrying`, `RetryCount` increments
8. Scheduler re-queues the operation
9. Processor loads retry rows (`State = Pending`, `MinRetryAttempt >= 1`)
10. Each row resumes from `RetryFromStepIndex`, skipping already-completed steps
11. After all rows, counters are recalculated from actual row record states
12. Final status: `Completed` (all succeeded) or `CompletedWithErrors` (some still failed)

---

## See Also

- [State Machine Guide](state-machine.md) -- Full state diagrams and walkthrough
- [Error Handling](error-handling.md) -- Error classification and querying
- [Step Operations](step-operations.md) -- Pipeline steps and per-step retry
