# Export Guide

BulkSharp provides an export feature for downloading operation results as CSV or JSON files. Exports support two modes (Report and Data) with optional filtering.

---

## Export Modes

### Report Mode

Exports a summary of each row's processing result: row number, state, step name, error details, and timestamps. Does not require `TrackRowData`.

Useful for: error analysis, audit reports, compliance documentation.

### Data Mode

Exports the full row data alongside processing results. Requires `TrackRowData = true` on the operation.

Useful for: reprocessing failed rows in external systems, data reconciliation.

---

## Export Formats

| Format | Content-Type | Description |
|--------|-------------|-------------|
| `Csv` | `text/csv` | Comma-separated values |
| `Json` | `application/json` | JSON array of objects |

---

## API Usage

### Via IBulkOperationService

```csharp
var result = await service.ExportAsync(operationId, new ExportRequest
{
    Mode = ExportMode.Report,
    Format = ExportFormat.Csv,
    State = RowRecordState.Failed,        // optional: filter by state
    ErrorType = BulkErrorType.StepFailure, // optional: filter by error type
    StepName = "Payment Capture",          // optional: filter by step name
    RowNumbers = [42, 87, 103]             // optional: filter by row numbers
});

// result.Stream   - the export file stream
// result.ContentType - "text/csv" or "application/json"
// result.FileName - e.g. "operation-{id}-report.csv"
// result.RowCount - number of rows in the export
```

### Query Filters

All filters on `ExportRequest` are optional and combine with AND logic:

| Filter | Type | Description |
|--------|------|-------------|
| `State` | `RowRecordState?` | Filter by row state (Pending, Running, Completed, Failed, TimedOut, WaitingForCompletion) |
| `ErrorType` | `BulkErrorType?` | Filter by error classification (Validation, Processing, StepFailure, Timeout, SignalFailure) |
| `StepName` | `string?` | Filter by step name (pipeline operations) |
| `RowNumbers` | `IReadOnlyList<int>?` | Filter to specific row numbers |

---

## Dashboard Endpoint

```
GET /api/bulks/{id}/export?mode=report&format=csv&state=Failed&errorType=StepFailure&stepName=Payment+Capture
```

| Parameter | Default | Values |
|-----------|---------|--------|
| `mode` | `report` | `report`, `data` |
| `format` | `csv` | `csv`, `json` |
| `state` | (none) | `Pending`, `Running`, `Completed`, `Failed`, `TimedOut`, `WaitingForCompletion` |
| `errorType` | (none) | `Validation`, `Processing`, `StepFailure`, `Timeout`, `SignalFailure` |
| `stepName` | (none) | Step name string |

The response is a file download with the appropriate content type and filename.

---

## TrackRowData Requirement

Data mode requires `TrackRowData = true` on the operation:

```csharp
[BulkOperation("import-users", TrackRowData = true)]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow> { ... }
```

If `TrackRowData` is not enabled and Data mode is requested, the export service throws an `InvalidOperationException`.

Report mode works regardless of `TrackRowData` -- it only uses row record metadata (state, errors, timestamps).

---

## Custom Export Formatter

The default formatter produces standard CSV and JSON output. To customize the output format, implement `IBulkExportFormatter` and register it:

### Interface

```csharp
public interface IBulkExportFormatter
{
    Task<Stream> FormatReportAsync(
        IAsyncEnumerable<BulkExportRow> rows,
        ExportRequest request,
        CancellationToken ct = default);

    Task<Stream> FormatDataAsync(
        IAsyncEnumerable<BulkExportRow> rows,
        ExportRequest request,
        CancellationToken ct = default);
}
```

### Implementation Example

```csharp
public class CustomExportFormatter : IBulkExportFormatter
{
    public async Task<Stream> FormatReportAsync(
        IAsyncEnumerable<BulkExportRow> rows, ExportRequest request, CancellationToken ct)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);

        await foreach (var row in rows.WithCancellation(ct))
        {
            // Custom format: pipe-delimited with your own column selection
            await writer.WriteLineAsync(
                $"{row.RowNumber}|{row.State}|{row.StepName}|{row.ErrorMessage}");
        }

        await writer.FlushAsync(ct);
        stream.Position = 0;
        return stream;
    }

    public async Task<Stream> FormatDataAsync(
        IAsyncEnumerable<BulkExportRow> rows, ExportRequest request, CancellationToken ct)
    {
        // row.RowData contains the serialized row JSON
        // row.RowType contains the CLR type for deserialization if needed
        // ...
    }
}
```

### Registration

```csharp
services.AddBulkSharp(builder => builder
    .UseExportFormatter<CustomExportFormatter>());
```

If no custom formatter is registered, the built-in `DefaultBulkExportFormatter` is used.

---

## BulkExportRow Fields

Each row in the export stream provides:

| Field | Type | Description |
|-------|------|-------------|
| `RowNumber` | `int` | Row position in the original file |
| `RowId` | `string?` | Business key set during validation |
| `State` | `RowRecordState` | Current state of the row |
| `StepName` | `string?` | Name of the current/last step |
| `StepIndex` | `int` | Step index (0-based for execution steps) |
| `ErrorType` | `BulkErrorType?` | Error classification if failed |
| `ErrorMessage` | `string?` | Error message if failed |
| `RetryAttempt` | `int` | Current retry attempt number |
| `CreatedAt` | `DateTime` | When the row record was created |
| `CompletedAt` | `DateTime?` | When the row completed (or failed) |
| `RowData` | `string?` | Serialized row data (Data mode, requires TrackRowData) |
| `RowType` | `Type?` | CLR type of the row (for deserialization) |

---

## See Also

- [Row Tracking](row-tracking.md) -- How row records are created and managed
- [Error Handling](error-handling.md) -- Error classification and querying
- [Dashboard](dashboard.md) -- Dashboard setup and API reference
