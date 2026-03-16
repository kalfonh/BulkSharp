# Error Handling

BulkSharp tracks errors at two levels: operation-level (metadata validation, scheduling) and row-level (validation, processing, step execution).

## Row-Level Errors

Every row is processed independently. A single row failure does not stop the operation -- it is recorded as a `BulkRowRecord` with an error state and processing continues with the next row.

Errors are classified via the `BulkErrorType` enum:

| Value | Meaning |
|-------|---------|
| `Validation` | Row failed `ValidateRowAsync` or a composed `IBulkRowValidator` |
| `Processing` | Row failed during `ProcessRowAsync` or simple row execution |
| `StepFailure` | A pipeline step exceeded its `MaxRetries` |
| `Timeout` | An async step timed out waiting for external completion |
| `SignalFailure` | An external signal reported a failure |

## Querying Errors

Errors are stored as `BulkRowRecord` entries with `ErrorType` set. Query them via `IBulkRowRecordRepository`:

```csharp
var errors = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorsOnly = true,                   // only records with errors
    ErrorType = BulkErrorType.Validation, // optional: filter by type
    RowNumber = 42,                       // optional: filter by row number
    RowId = "ORD-123",                   // optional: filter by business key
    Page = 1,
    PageSize = 50,
    SortBy = "RowNumber",
    SortDescending = false
});

Console.WriteLine($"Total errors: {errors.TotalCount}");
Console.WriteLine($"Has more pages: {errors.HasNextPage}");

foreach (var record in errors.Items)
{
    Console.WriteLine($"Row {record.RowNumber} [{record.ErrorType}]: {record.ErrorMessage}");
}
```

## Including Row Data in Errors

By default, error records do not include the row data that caused the error. To include it, set `TrackRowData = true` on the operation attribute:

```csharp
[BulkOperation("import-users", TrackRowData = true)]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow> { ... }
```

When enabled, `record.RowData` contains the JSON-serialized row stored during the validation phase. **Warning**: this may contain PII. Disable in production if rows contain sensitive data.

You can also enable row data globally:

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.IncludeRowDataInErrors = true));
```

## Operation-Level Errors

When metadata validation fails or an unrecoverable error occurs, the entire operation is marked as `Failed`:

```csharp
var operation = await service.GetBulkOperationAsync(operationId);

if (operation!.Status == BulkOperationStatus.Failed)
{
    Console.WriteLine($"Operation failed: {operation.ErrorMessage}");
}
```

## Operation Status Transitions

| Status | Meaning |
|--------|---------|
| `Pending` | Created, waiting to be processed |
| `Running` | Currently processing rows |
| `Completed` | All rows processed successfully |
| `CompletedWithErrors` | Processing finished but some rows failed |
| `Failed` | Operation-level failure (metadata validation, file error, etc.) |
| `Cancelled` | Cancelled by user |

## Batch Error Writing

Row records (including errors) are written in batches for performance. The `FlushBatchSize` option (default: 100) controls how many rows are processed between flushes. This means errors may not appear in queries immediately during processing -- they are flushed periodically and at operation completion.
