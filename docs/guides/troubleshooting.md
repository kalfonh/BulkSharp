# Troubleshooting Guide

Practical solutions for common issues when running BulkSharp in development and production.

## Common Issues

### Operation Stuck in "Running" Status

**Symptoms:** An operation shows `BulkOperationStatus.Running` indefinitely and never transitions to `Completed`, `CompletedWithErrors`, or `Failed`.

**Possible causes:**

1. **Async step waiting for a signal that never arrives.** If you use signal-based async steps (`StepCompletionMode.Signal`), the step blocks until an external system calls `SignalSuccessAsync` or `SignalFailureAsync`. If the external system is down or misconfigured, the step hangs.

   **Fix:** Enable orphaned step recovery so that steps stuck in `WaitingForCompletion` are cleaned up on application restart:
   ```csharp
   services.AddBulkSharp(builder => builder
       .ConfigureOptions(opts => opts.EnableOrphanedStepRecovery = true));
   ```

2. **Scheduler worker exhaustion.** The `ChannelsScheduler` processes operations with a fixed number of workers (`WorkerCount`). If all workers are occupied by long-running operations, new operations queue up.

   **Fix:** Increase the worker count:
   ```csharp
   builder.UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 8));
   ```

3. **Unhandled exception in the processing pipeline.** Check application logs for exceptions. The processor catches row-level exceptions and records them as errors, but infrastructure-level failures (database unavailable, storage errors) may cause the operation to stall.

   **Fix:** Check logs and ensure storage and database connectivity is healthy.

### No Rows Processed (0 Processed, 0 Failed)

**Symptoms:** Operation completes but `TotalRows`, `ProcessedRows`, and `FailedRows` are all 0.

**Possible causes:**

1. **Empty file.** The uploaded file contains no data rows (only headers or completely empty).

   **Fix:** Validate the file before submission using the pre-submission validation endpoint:
   ```
   POST /api/bulks/validate
   ```

2. **Wrong data format processor.** The file extension does not match the actual format, or a custom `IDataFormatProcessor<T>` is not registered.

   **Fix:** Verify the file extension matches the content. BulkSharp selects the processor based on `operation.FileName`. CSV files must end in `.csv`, JSON files in `.json`.

3. **CSV column mapping mismatch.** The `[CsvColumn]` attributes on your row class do not match the column headers in the file. CsvHelper silently produces rows with all null/default values.

   **Fix:** Check that `[CsvColumn("Header Name")]` attributes exactly match the CSV headers (case-sensitive by default).

### Validation Errors on Valid Data

**Symptoms:** Rows that appear correct are rejected by `ValidateRowAsync`.

**Possible causes:**

1. **Type conversion failures during parsing.** The CSV or JSON parser fails to convert a field value to the target CLR type. This happens before your validation code runs.

   **Fix:** Check that numeric fields do not contain currency symbols, date fields use the expected format, and boolean fields use values the parser recognizes.

2. **Metadata validators rejecting rows.** Registered `IBulkMetadataValidator<TMetadata>` implementations may impose constraints you are not aware of.

   **Fix:** Review all registered metadata validators in your DI configuration.

3. **Null reference on optional fields.** If your row class has non-nullable reference types and the CSV has empty cells, the parser may assign null, causing NullReferenceException during validation.

   **Fix:** Mark optional fields as nullable (`string?`) in your row class.

### EF Core Concurrency Errors

**Symptoms:** `DbUpdateConcurrencyException` during operation processing, particularly with parallel row processing enabled.

**Possible causes:**

1. **RowVersion conflict on BulkOperation updates.** BulkSharp uses optimistic concurrency via `RowVersion` on `BulkOperation`. When multiple consumers update the operation concurrently (e.g., recording row results), conflicts can occur.

   **Fix:** This is handled internally via `Interlocked` operations on `RecordRowResult`. If you see this error, ensure you are not manually updating `BulkOperation` entities outside of BulkSharp's pipeline.

2. **Multiple application instances processing the same operation.** If two instances of your app pick up the same queued operation, they will conflict.

   **Fix:** Ensure only one instance of `ChannelsScheduler` is running per operation queue, or use a distributed lock if running multiple instances.

### S3 Connection Failures

**Symptoms:** `AmazonS3Exception` or timeout errors when uploading or retrieving files.

**Possible causes:**

1. **Incorrect endpoint or credentials.** When using LocalStack for development, the service URL must point to the LocalStack endpoint, not AWS.

   **Fix:** Verify your S3 configuration:
   ```csharp
   builder.UseFileStorage(fs => fs.UseS3(opts =>
   {
       opts.BucketName = "my-bucket";
       opts.Region = "us-east-1";
       opts.ServiceUrl = "http://localhost:4566";  // LocalStack
       opts.ForcePathStyle = true;                  // Required for LocalStack
   }));
   ```

2. **Bucket does not exist.** S3 storage does not auto-create buckets.

   **Fix:** Create the bucket before starting the application, or use an initialization script.

3. **IAM permissions.** The IAM role or credentials lack `s3:PutObject`, `s3:GetObject`, or `s3:DeleteObject` permissions on the target bucket.

   **Fix:** Verify the IAM policy grants the required permissions.

### File Size Limit Exceeded

**Symptoms:** Operation is rejected immediately with an error about file size.

`BulkSharpOptions.MaxFileSizeBytes` defaults to 100 MB. Files exceeding this limit are rejected at submission time.

**Fix:** Increase the limit or disable it:
```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts =>
    {
        opts.MaxFileSizeBytes = 500 * 1024 * 1024;  // 500 MB
        // opts.MaxFileSizeBytes = 0;                // Disable limit entirely
    }));
```

Note: Disabling the limit entirely is not recommended in production. Large files consume significant memory during processing.

## Debugging Tips

### Enable Detailed Logging

BulkSharp uses `Microsoft.Extensions.Logging` with source-generated `LoggerMessage` methods. To see detailed processing output:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "BulkSharp": "Debug",
      "BulkSharp.Processing": "Debug"
    }
  }
}
```

Key log events to look for:
- Step retry attempts (`RetryingStep`)
- Step failures (`StepFailedWillRetry`, `StepFailedAllRetries`)
- Step-based operation discovery (`ProcessingStepBasedOperation`)

### Check Operation Status Programmatically

```csharp
var operation = await service.GetBulkOperationAsync(operationId);

if (operation is null)
{
    // Operation ID is invalid or was not persisted
    return;
}

Console.WriteLine($"Status: {operation.Status}");
Console.WriteLine($"Total rows: {operation.TotalRows}");
Console.WriteLine($"Processed: {operation.ProcessedRows}");
Console.WriteLine($"Failed: {operation.FailedRows}");
Console.WriteLine($"Error: {operation.ErrorMessage}");
```

### Query Errors for a Specific Operation

```csharp
var errors = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorsOnly = true,
    PageSize = 100,
    SortBy = "RowNumber"
});

foreach (var record in errors.Items)
{
    Console.WriteLine($"Row {record.RowNumber} [{record.ErrorType}]: {record.ErrorMessage}");
    if (record.RowData is not null)
        Console.WriteLine($"  Data: {record.RowData}");
}
```

### Inspect Step Status for a Row

Query `BulkRowRecord` entries for a specific row to see all steps:

```csharp
var rowRecords = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    RowNumber = 42  // specific row
});

foreach (var record in rowRecords.Items.OrderBy(r => r.StepIndex))
{
    Console.WriteLine($"  Step {record.StepIndex} ({record.StepName}): {record.State}");
    if (record.ErrorMessage is not null)
        Console.WriteLine($"    Error: {record.ErrorMessage}");
}
```

## Performance Issues

### Tuning FlushBatchSize

The default `FlushBatchSize` of 100 is a reasonable starting point. Adjust based on your workload:

| Scenario | Recommended FlushBatchSize | Rationale |
|----------|---------------------------|-----------|
| Small files (< 1,000 rows) | 50 or lower | Errors visible quickly, low overhead |
| Large files (100K+ rows) | 200-500 | Reduces database round-trips |
| High error rate (> 20%) | 50-100 | Keep error visibility close to real-time |
| Low error rate (< 1%) | 500+ | Most flushes write few or no errors |

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.FlushBatchSize = 200));
```

### Tuning MaxRowConcurrency

`MaxRowConcurrency` controls how many rows are processed in parallel. The default is 1 (sequential).

| Scenario | Recommended MaxRowConcurrency |
|----------|-------------------------------|
| CPU-bound row processing | 1 (sequential) |
| I/O-bound row processing (HTTP calls, DB writes) | 4-16 |
| Async steps with external waits | 8-32 |
| Operations with ordering requirements | 1 (sequential) |

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 8));
```

**Warning:** Higher concurrency increases memory usage. Each concurrent row holds its data in memory while processing. The bounded channel capacity is `MaxRowConcurrency * 2`, which limits how far ahead the file reader can get.

### Large File Processing

For files over 100K rows:

1. **Increase FlushBatchSize** to reduce database write frequency.
2. **Increase MaxRowConcurrency** if row processing is I/O-bound.
3. **Monitor memory usage.** BulkSharp streams rows via `IAsyncEnumerable<T>`, so the full file is not loaded into memory. However, pending errors and step statuses are buffered in memory until flushed.
4. **Consider MaxFileSizeBytes.** The default 100 MB limit may need adjustment for large datasets.

## Dashboard Issues

### Dashboard Not Loading

**Symptoms:** The dashboard Blazor components do not render, or you get a 404.

**Possible causes:**

1. **Missing middleware registration.** The dashboard requires both service registration and endpoint mapping.

   **Fix:** Ensure both are present:
   ```csharp
   // In service registration
   services.AddBulkSharp(builder => { /* ... */ });
   services.AddBulkSharpDashboard();

   // In middleware pipeline
   app.MapBulkSharpDashboard();
   ```

2. **Static files not served.** The dashboard is a Razor Class Library (RCL) that ships static assets.

   **Fix:** Ensure `app.UseStaticFiles()` is called before mapping the dashboard.

3. **Blazor Server not configured.** The dashboard uses Blazor Server for real-time updates.

   **Fix:** Ensure `services.AddServerSideBlazor()` and `app.MapBlazorHub()` are registered.

### API Returning Empty Results

**Symptoms:** The dashboard shows no operations, or API calls to `/api/bulks` return empty arrays.

**Possible causes:**

1. **In-memory metadata storage with application restart.** If using `UseInMemory()` for metadata storage, all data is lost on restart.

   **Fix:** Use persistent storage (SQL Server via EF Core) for production:
   ```csharp
   builder.UseMetadataStorage(ms => ms.UseEntityFramework<MyDbContext>(opts =>
       opts.ConnectionString = connectionString));
   ```

2. **Wrong operation name filter.** The dashboard queries by operation name. If operations were registered under a different name, they will not appear.

   **Fix:** Verify the `[BulkOperation("name")]` attribute matches what you expect in the dashboard.

3. **Operations still pending.** Newly created operations start in `Pending` status. They may not appear in filtered views that only show completed operations.

   **Fix:** Check unfiltered results or include `Pending` in your status filter.
