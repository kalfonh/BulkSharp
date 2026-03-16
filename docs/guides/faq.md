# FAQ

## General

### Can I pause or cancel a running operation?

Not directly. BulkSharp processes operations to completion once started. To stop processing, shut down the host. The operation will remain in `Processing` status and can be detected as stale on restart.

Cancellation support is planned for a future release.

### Can I re-process failed rows?

Not automatically. You can query failed rows via the error repository, fix the source data, and submit a new operation. Each operation is immutable once created.

### What happens if the process crashes mid-operation?

The operation remains in `Running` status. Any errors flushed before the crash are persisted. Unflushed errors (within the current `FlushBatchSize` batch) are lost.

To recover automatically, set `StuckOperationTimeout` on the `ChannelsScheduler`:

```csharp
s.UseChannels(opts =>
{
    opts.StuckOperationTimeout = TimeSpan.FromMinutes(10);
});
```

On startup and each poll cycle, operations stuck in `Running` beyond the timeout are marked `Failed`. You can then resubmit them or investigate the errors.

### Does BulkSharp support transactions?

No. Each row is processed independently. If row 50 fails, rows 1-49 are already processed. Use step-based operations with idempotent steps if you need retry capability.

## File Handling

### What file formats are supported?

CSV and JSON. The format is detected by file extension (`.csv` or `.json`). You can add custom formats by implementing `IDataFormatProcessor<T>`.

### How large can uploaded files be?

Default limit is 100 MB, configurable via `BulkSharpOptions.MaxFileSizeBytes`. Set to `0` to disable the limit. Files are streamed, so memory usage stays constant regardless of file size.

### Where are uploaded files stored?

Depends on your file storage provider:
- **FileSystem** (default): Local disk at `bulksharp-storage/`
- **InMemory**: In-process memory (testing only)
- **S3**: Amazon S3 bucket
- **Custom**: Your implementation of `IFileStorageProvider`

## Operations

### What's the difference between Failed and CompletedWithErrors?

- **Failed**: The operation itself failed (unhandled exception, metadata validation failure). No rows were processed, or processing was aborted.
- **CompletedWithErrors**: All rows were attempted. Some succeeded, some failed validation or processing. The operation completed normally.

### How do I query operation errors?

```csharp
var errors = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorsOnly = true,
    Page = 1,
    PageSize = 50
});
```

Or via the Dashboard REST API: `GET /api/bulks/{id}/errors`

### Can I track individual row status?

Yes. Every row gets one or more `BulkRowRecord` entries (one for validation at StepIndex=-1, one per execution step at StepIndex>=0). Query via `IBulkRowRecordRepository`:

```csharp
var rows = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorsOnly = true,
    ErrorType = BulkErrorType.Validation
});
```

To also store the raw row data (JSON), set `TrackRowData = true` on the `[BulkOperation]` attribute.

### What is the difference between simple and step-based operations?

- **Simple** (`IBulkRowOperation`): Each row is processed in a single `ProcessRowAsync` call.
- **Step-based** (`IBulkPipelineOperation`): Each row passes through an ordered sequence of steps. Each step can have its own retry count, and async steps can wait for external signals.

Use step-based operations when processing involves multiple stages, external system calls, or approval workflows.

## Scheduling

### How many operations run concurrently?

Controlled by `ChannelsSchedulerOptions.WorkerCount` (default depends on configuration). Each worker processes one operation at a time. Within each operation, `MaxRowConcurrency` controls row-level parallelism.

### Can I run the API and Worker as separate processes?

Yes. Use `AddBulkSharpApi()` in the API process and `AddBulkSharp()` with `PendingPollInterval` in the Worker. The API stores operations as `Pending`; the Worker polls the database and processes them. See [API + Worker Architecture](../getting-started/api-worker.md).

### Can I use a message queue instead of the built-in scheduler?

Yes. Implement `IBulkScheduler` and register it via `UseCustom<T>()`. Your implementation can read from SQS, RabbitMQ, or any other queue.

## Notifications

### Can I get notified when an operation completes?

Yes. Implement `IBulkOperationEventHandler` — it's auto-discovered from scanned assemblies, no registration needed:

```csharp
public class MyHandler : IBulkOperationEventHandler
{
    public async Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        // Your logic here
    }
}
```

Available events: Created, StatusChanged, Completed, Failed, RowFailed. Handlers run fire-and-forget — a failing handler never blocks processing.

## Dashboard

### Does the dashboard require authentication?

No. BulkSharp does not include authentication. Add authentication middleware in your ASP.NET Core pipeline before mapping the dashboard.

### Can I embed the dashboard in an existing Blazor app?

Yes. The dashboard is a Razor Class Library. Call `AddBulkSharpDashboard()` and `UseBulkSharpDashboard()` in your existing app. The dashboard mounts at `/bulksharp` by default.

### Can I add custom endpoints to the dashboard?

Yes. Use the `configureAdditionalEndpoints` parameter:

```csharp
app.UseBulkSharpDashboard(configureAdditionalEndpoints: endpoints =>
{
    endpoints.MapGet("/api/custom", () => "Hello");
});
```

## Entity Framework

### Do I need to run migrations?

If using `EnsureCreatedAsync()` (as in the Production sample), no. For production databases, generate and apply EF Core migrations from `BulkSharpDbContext`.

### Can I use my own DbContext?

Yes. Create a DbContext that inherits from `BulkSharpDbContext` and register it:

```csharp
services.AddBulkSharpEntityFramework<MyAppDbContext>();
```

### Does BulkSharp support databases other than SQL Server?

The EF provider uses `UseSqlServer()` by default, but `BulkSharpDbContext` is a standard EF Core `DbContext`. You can configure it with any EF Core provider (PostgreSQL, SQLite, etc.) by using `AddBulkSharpEntityFramework<T>()` with custom options.
