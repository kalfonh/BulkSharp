# Configuration

BulkSharp is configured through the `AddBulkSharp` builder and `BulkSharpOptions`.

## Builder API

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => { /* BulkSharpOptions */ })
    .UseFileStorage(fs => { /* file storage */ })
    .UseMetadataStorage(ms => { /* metadata storage */ })
    .UseScheduler(s => { /* scheduler */ }));
```

Each axis has a default if not configured:
- **File storage**: Local filesystem (`bulksharp-storage/`)
- **Metadata storage**: In-memory
- **Scheduler**: Channels-based background processing

## BulkSharpOptions

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts =>
    {
        opts.MaxFileSizeBytes = 100 * 1024 * 1024;  // 100 MB (default)
        opts.MaxRowConcurrency = 1;                   // Sequential (default)
        opts.FlushBatchSize = 100;                    // Rows between flushes (default)
        opts.IncludeRowDataInErrors = false;           // PII risk if true (default)
        opts.EnableOrphanedStepRecovery = false;       // For signal-based steps (default)
    }));
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxFileSizeBytes` | `long` | `104857600` (100 MB) | Maximum upload file size. Set to `0` to disable. |
| `MaxRowConcurrency` | `int` | `1` | Maximum rows processed in parallel. See [Parallel Processing](parallel-processing.md). |
| `FlushBatchSize` | `int` | `100` | Number of rows between progress flushes (error batch writes + status updates). |
| `IncludeRowDataInErrors` | `bool` | `false` | Whether to serialize row data into error records. **Warning**: may contain PII. |
| `EnableOrphanedStepRecovery` | `bool` | `false` | Recover rows stuck in `WaitingForCompletion` after restart. Only needed for signal-based async steps. |
| `MaxRetryAttempts` | `int` | `10` | Maximum number of operation-level retry attempts. Set to `0` to disable the limit. See [Retry Guide](retry.md). |

## Convenience Methods

```csharp
// All defaults (file system + in-memory metadata + Channels scheduler)
services.AddBulkSharp();

// All in-memory + immediate scheduler (testing)
services.AddBulkSharpInMemory();

// API-only: no worker threads, no hosted services, operations stay Pending
services.AddBulkSharpApi();

// API-only with custom storage
services.AddBulkSharpApi(builder => builder
    .UseFileStorage(fs => fs.UseS3(opts => opts.BucketName = "uploads"))
    .UseMetadataStorage(ms => ms.UseSqlServer(opts =>
        opts.ConnectionString = connectionString)));
```

`AddBulkSharpApi()` registers `IBulkOperationService`, `IBulkOperationQueryService`, storage providers, and data format processors. It uses a built-in `NullBulkScheduler` that leaves operations in `Pending` status for a separate Worker process. See [API + Worker Architecture](../getting-started/api-worker.md).

## File Storage Options

| Method | Description |
|--------|-------------|
| `fs.UseFileSystem(basePath?)` | Local filesystem. Default path: `bulksharp-storage` |
| `fs.UseInMemory()` | In-memory storage (testing only) |
| `fs.UseS3(opts => ...)` | Amazon S3. Requires `BulkSharp.Files.S3` package. |
| `fs.UseCustom<T>()` | Custom `IFileStorageProvider` implementation |

## Metadata Storage Options

| Method | Description |
|--------|-------------|
| `ms.UseInMemory()` | In-memory repositories (default) |

For SQL Server persistence, register separately:

```csharp
// SQL Server with typed options
services.AddBulkSharpSqlServer(opts =>
{
    opts.ConnectionString = connectionString;
    opts.MaxRetryCount = 5;
    opts.MaxRetryDelay = TimeSpan.FromSeconds(30);
});

// Custom DbContext
services.AddBulkSharpEntityFramework<MyDbContext>(options =>
    options.UseSqlServer(connectionString));
```

Requires the `BulkSharp.Data.EntityFramework` package.

## Scheduler Options

| Method | Description |
|--------|-------------|
| `s.UseChannels(opts?)` | Background processing via `System.Threading.Channels` (default) |
| `s.UseImmediate()` | Synchronous inline execution (testing only) |
| `s.UseCustom<T>()` | Custom `IBulkScheduler` implementation |

The Channels scheduler accepts:

```csharp
s.UseChannels(opts =>
{
    opts.WorkerCount = 4;                                  // Default: 4
    opts.PendingPollInterval = TimeSpan.FromSeconds(5);    // Default: null (disabled)
});
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `WorkerCount` | `int` | `4` | Concurrent operation workers. |
| `QueueCapacity` | `int` | `1000` | Bounded channel capacity. |
| `ShutdownTimeout` | `TimeSpan` | `30s` | Grace period before force-cancelling workers. |
| `PendingPollInterval` | `TimeSpan?` | `null` | Interval for polling the database for new Pending operations. `null` disables polling. Set this when using a separate API process with `AddBulkSharpApi()`. |
| `StuckOperationTimeout` | `TimeSpan?` | `null` | Operations stuck in Running beyond this duration are marked Failed on startup and each poll cycle. `null` disables. Set this in Worker processes to recover from crashes. |

`WorkerCount` controls how many operations process concurrently (not rows - see `MaxRowConcurrency` for row-level parallelism).

## Event Hooks

Event handlers, validators, and processors are **auto-discovered** from scanned assemblies. Just implement the interface — no manual registration needed:

```csharp
// Auto-discovered: just create the class and it's registered
public class EmailNotificationHandler : IBulkOperationEventHandler
{
    public async Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        // Send email notification
    }

    public async Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct)
    {
        // Send failure alert
    }
}
```

Available events: `OnOperationCreatedAsync`, `OnStatusChangedAsync`, `OnOperationCompletedAsync`, `OnOperationFailedAsync`, `OnRowFailedAsync`.

Handlers run in parallel. A failing handler is logged but never blocks processing.

For explicit control, you can still use `builder.AddEventHandler<T>()`:

```csharp
services.AddBulkSharp(builder => builder
    .AddEventHandler<EmailNotificationHandler>());
```

## Auto-Discovery

BulkSharp scans assemblies for implementations of these extensibility interfaces and registers them in DI automatically:

| Interface | Purpose | Runs |
|---|---|---|
| `IBulkOperationEventHandler` | Lifecycle events (created, completed, failed) | On state transitions |
| `IBulkMetadataValidator<TMetadata>` | Validates operation metadata | Before processing starts |
| `IBulkRowValidator<TMetadata, TRow>` | Cross-cutting row validation | Before each row is processed |
| `IBulkRowProcessor<TMetadata, TRow>` | Post-processing hook per row | After each row is processed |

Multiple implementations of each interface can coexist — they all run. This enables composable cross-cutting concerns without modifying operation code:

```csharp
// Validates department is from an allowed list — auto-discovered, runs before operation starts
public class DepartmentAllowlistValidator : IBulkMetadataValidator<UserImportMetadata>
{
    public Task ValidateAsync(UserImportMetadata metadata, CancellationToken ct = default)
    {
        if (!AllowedDepartments.Contains(metadata.Department))
            throw new ArgumentException($"Department '{metadata.Department}' is not allowed");
        return Task.CompletedTask;
    }
}

// Validates email domain per row — auto-discovered, runs before each row
public class CorporateEmailValidator : IBulkRowValidator<UserImportMetadata, UserImportRow>
{
    public Task ValidateAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken ct = default)
    {
        var domain = row.Email.Split('@').LastOrDefault();
        if (domain != "example.com")
            throw new ArgumentException($"Email domain '{domain}' is not allowed");
        return Task.CompletedTask;
    }
}

// Audit log after each row — auto-discovered, runs after processing
public class AuditProcessor(ILogger<AuditProcessor> logger)
    : IBulkRowProcessor<UserImportMetadata, UserImportRow>
{
    public Task ProcessAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken ct = default)
    {
        logger.LogInformation("[audit] Imported {Email} by {ImportedBy}", row.Email, metadata.ImportedBy);
        return Task.CompletedTask;
    }
}
```

Assembly scanning uses the same scope as operation discovery — either all loaded assemblies (default) or the assemblies specified via `AddOperationsFromAssembly()`.

## Export Formatter

By default, BulkSharp uses a built-in formatter for CSV and JSON exports. To customize export output, register a custom `IBulkExportFormatter`:

```csharp
services.AddBulkSharp(builder => builder
    .UseExportFormatter<CustomExportFormatter>());
```

See [Export Guide](export.md) for details on implementing custom formatters.

## Row Tracking

BulkSharp tracks every row's status via `BulkRowRecord` entries. By default, only status, timestamps, and errors are stored. To also persist the raw row data, set `TrackRowData` on the operation attribute:

```csharp
[BulkOperation("import-users", TrackRowData = true)]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow> { ... }
```

Query row records via `IBulkRowRecordRepository`:

```csharp
var rows = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorsOnly = true,
    Page = 1,
    PageSize = 50
});
```
