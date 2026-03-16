# Production Deployment

This guide covers considerations for running BulkSharp in production environments.

## Storage Provider Selection

| Scenario | File Storage | Metadata Storage |
|----------|-------------|------------------|
| Single server, moderate volume | FileSystem | SQL Server (EF) |
| Multi-server / containerized | S3 | SQL Server (EF) |
| Serverless / ephemeral compute | S3 | SQL Server (EF) |
| Development / testing | InMemory | InMemory |

> [!IMPORTANT]
> In-memory storage is not durable. Never use it in production.

## Database Configuration

### Connection Pooling

BulkSharp uses `IDbContextFactory<T>` to create short-lived `DbContext` instances per repository call. This makes all EF repositories thread-safe for parallel row processing but increases connection pool pressure.

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 8)
    .UseMetadataStorage(ms => ms.UseSqlServer(sql =>
    {
        sql.ConnectionString = "Server=...;Max Pool Size=200;";
        sql.MaxRetryCount = 5;
        sql.MaxRetryDelay = TimeSpan.FromSeconds(10);
    })));
```

**Sizing guideline:** Set `Max Pool Size` to at least `MaxRowConcurrency * WorkerCount * 2` to avoid pool exhaustion under load.

### Optimistic Concurrency

`BulkOperation` uses a `RowVersion` column for optimistic concurrency. The EF repository retries up to 5 times on `DbUpdateConcurrencyException`, merging monotonically-increasing counters (ProcessedRows, SuccessfulRows, FailedRows) from the database on each retry.

No configuration is needed. If you see frequent concurrency retries in logs, reduce `MaxRowConcurrency` or increase `FlushBatchSize` to reduce write frequency.

## Scheduler Configuration

### Channels Scheduler

The default `ChannelsScheduler` runs as an `IHostedService` with a bounded channel.

```csharp
builder.UseScheduler(s => s.UseChannels(opts =>
{
    opts.WorkerCount = 4;       // Concurrent operations
    opts.QueueCapacity = 1000;  // Bounded queue size
}));
```

- **WorkerCount** controls how many operations process simultaneously. Each worker processes one operation at a time; within each operation, `MaxRowConcurrency` controls row-level parallelism.
- **QueueCapacity** bounds the in-memory queue. If the queue is full, `ScheduleBulkOperationAsync` blocks until space is available.

**Total thread pressure:** `WorkerCount * MaxRowConcurrency` is the maximum number of concurrent row-processing tasks.

## S3 File Storage

### IAM Permissions

The S3 provider requires `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject`, and `s3:ListObjectsV2` on the configured bucket.

### Key Format

Files are stored as `{prefix}{guid}-{sanitizedFileName}`. The prefix is configurable via `S3StorageOptions.Prefix`.

### Connection Handling

The `S3StorageProvider` wraps `GetObjectResponse` in a custom stream that disposes the response when the stream is closed, preventing HTTP connection pool exhaustion. Callers must dispose the returned stream.

## Monitoring

### Structured Logging

BulkSharp uses source-generated `LoggerMessage` throughout the Processing layer. Key log events:

| Event | Level | When |
|-------|-------|------|
| Operation created | Information | New operation submitted |
| Processing started | Information | Worker picks up operation |
| Row validation failed | Warning | Individual row fails validation |
| Processing completed | Information | All rows processed |
| Processing failed | Error | Unhandled exception during processing |
| Step retry | Warning | Step failed, retrying with backoff |
| Concurrency conflict | Warning | Optimistic concurrency retry in EF |

### Diagnostics

`BulkSharpDiagnostics` provides diagnostic event names for integration with `System.Diagnostics.Activity` and OpenTelemetry.

### Health Checks

BulkSharp does not register health checks automatically. For production, consider adding:

```csharp
// Verify database connectivity
services.AddHealthChecks()
    .AddDbContextCheck<BulkSharpDbContext>("bulksharp-db");

// Verify S3 connectivity (custom check)
services.AddHealthChecks()
    .AddCheck("bulksharp-s3", () =>
    {
        // Attempt a lightweight S3 operation
        return HealthCheckResult.Healthy();
    });
```

## Error Batching

Errors are buffered in memory and flushed to the repository in batches controlled by `FlushBatchSize` (default: 100). This reduces database round-trips during processing.

- After every `FlushBatchSize` rows, pending errors are written and the operation status is updated.
- On operation completion or failure, remaining errors are flushed.
- If the process crashes mid-operation, unflushed errors are lost. The operation will remain in `Processing` status and can be detected by monitoring for stale operations.

## Scaling Considerations

- **Vertical scaling:** Increase `WorkerCount` and `MaxRowConcurrency` on a single host. Monitor CPU, memory, and database connection usage.
- **Horizontal scaling:** Run multiple instances with the same database and S3 bucket. Each instance runs its own `ChannelsScheduler`. Use an external queue (e.g., SQS) to distribute work if needed — implement a custom `IBulkScheduler` that reads from the queue.
- **Large files:** BulkSharp streams files via `IAsyncEnumerable<T>`, keeping memory usage constant regardless of file size. The `MaxFileSizeBytes` option (default: 100 MB) limits upload size.

## Security

### PII in Error Records

`BulkSharpOptions.IncludeRowDataInErrors` (default: `false`) controls whether raw row data is serialized into error records. Enabling this is useful for debugging but may store PII in the database.

### File Storage

- Uploaded files are stored as-is with no encryption at rest by BulkSharp. Use S3 server-side encryption or encrypted EBS volumes for the FileSystem provider.
- File names are sanitized via `Path.GetFileName()` to prevent path traversal attacks.

### Input Validation

- `MaxFileSizeBytes` limits upload size at the stream level.
- CSV and JSON parsers use streaming and do not load entire files into memory.
- The pre-submission validation endpoint validates metadata and the first row without processing.
