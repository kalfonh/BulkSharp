# Row Tracking

BulkSharp tracks every row processed through an operation via a unified `BulkRowRecord` model. One table covers the full row lifecycle -- validation, processing steps, errors, and async completion -- without the overhead of separate tables.

## How It Works

Processing uses a two-pass streaming model:

**Pass 1 (Validating)**: The file is streamed row by row. Each row is validated and a `BulkRowRecord` is created with `StepIndex = -1` (validation) and `Pending` state. Rows that fail validation are immediately marked `Failed` with the error message and `BulkErrorType`. Row records are batch-inserted for efficiency.

**Pass 2 (Processing)**: The file is streamed again. Rows that failed validation are skipped. Each valid row is executed. For simple operations, a `BulkRowRecord` with `StepIndex = 0` is created. For pipeline operations, one record per step (`StepIndex = 0, 1, 2, ...`).

Both passes stream without buffering -- memory stays flat regardless of file size.

### BulkRowRecord Lifecycle

```
Pending -> Running -> Completed
                   -> Failed
                   -> WaitingForCompletion -> Completed (async steps)
                                           -> TimedOut
```

### RowRecordState

| State | Meaning |
|-------|---------|
| `Pending` | Record created, not yet processed |
| `Running` | Currently being executed |
| `Completed` | Executed successfully |
| `Failed` | Failed validation or processing (see `ErrorMessage`, `ErrorType`) |
| `WaitingForCompletion` | Async step waiting for external signal or poll |
| `TimedOut` | Async step exceeded timeout |

### BulkErrorType

When a record is in `Failed` state, `ErrorType` classifies the failure:

| Value | Meaning |
|-------|---------|
| `Validation` | Row failed validation (metadata or row-level) |
| `Processing` | Row failed during execution |
| `StepFailure` | A pipeline step failed after all retries |
| `Timeout` | Async step timed out waiting for completion |
| `SignalFailure` | External signal reported a failure |

## BulkRowRecord Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Unique identifier |
| `BulkOperationId` | `Guid` | Parent operation |
| `RowNumber` | `int` | 1-based row position in the file |
| `RowId` | `string?` | Business key from `IBulkRow.RowId` |
| `StepName` | `string` | Step name (`"validation"` for StepIndex=-1) |
| `StepIndex` | `int` | `-1` for validation, `0+` for execution steps |
| `State` | `RowRecordState` | Current lifecycle state |
| `ErrorType` | `BulkErrorType?` | Error classification (null when no error) |
| `ErrorMessage` | `string?` | Error details if failed |
| `RowData` | `string?` | Serialized row data as JSON (only when `TrackRowData = true`) |
| `SignalKey` | `string?` | Signal key for async step completion |
| `CreatedAt` | `DateTime` | When the record was created |
| `StartedAt` | `DateTime?` | When execution started |
| `CompletedAt` | `DateTime?` | When execution finished |

## StepIndex Convention

- `StepIndex = -1` -- Validation-phase record. One per row. Tracks whether the row passed or failed validation.
- `StepIndex >= 0` -- Execution-phase record. For simple operations (`IBulkRowOperation`), there is one record at `StepIndex = 0`. For pipeline operations (`IBulkPipelineOperation`), one record per step.

## TrackRowData

By default, `BulkRowRecord.RowData` is `null` -- only status and error information are stored. To also persist the raw row data as serialized JSON, set `TrackRowData = true` on the operation attribute:

```csharp
[BulkOperation("import-users", TrackRowData = true)]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow> { ... }
```

When enabled, each row is serialized to JSON during the Validating phase and stored in the validation record's `RowData` field. This is useful for:
- Debugging failed rows without re-reading the file
- Displaying row data in the Dashboard UI
- Audit trails

**Trade-off**: Adds serialization cost per row and increases storage. For large files with simple rows this is negligible. For large files with complex rows, measure the impact.

## Querying Row Records

Inject `IBulkRowRecordRepository` and use `BulkRowRecordQuery`:

```csharp
// Get all failed rows (errors only)
var errors = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorsOnly = true,
    Page = 1,
    PageSize = 50
});

// Get validation failures only
var validationErrors = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    ErrorType = BulkErrorType.Validation,
    StepIndex = -1
});

// Get all records for a specific row (validation + all steps)
var rowRecords = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    RowNumber = 42
});

// Get rows in a specific range
var range = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    FromRowNumber = 100,
    ToRowNumber = 200
});

// Get rows waiting for async completion
var waiting = await rowRecordRepo.QueryAsync(new BulkRowRecordQuery
{
    OperationId = operationId,
    State = RowRecordState.WaitingForCompletion
});
```

### BulkRowRecordQuery Filters

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OperationId` | `Guid` | (required) | Filter by parent operation |
| `RowNumber` | `int?` | `null` | Filter by specific row number |
| `RowNumbers` | `IReadOnlyList<int>?` | `null` | Filter by multiple row numbers |
| `RowId` | `string?` | `null` | Filter by business key |
| `StepIndex` | `int?` | `null` | Filter by step index (-1 = validation) |
| `StepName` | `string?` | `null` | Filter by step name |
| `State` | `RowRecordState?` | `null` | Filter by lifecycle state |
| `ErrorType` | `BulkErrorType?` | `null` | Filter by error classification |
| `ErrorsOnly` | `bool?` | `null` | When true, only records with errors |
| `FromRowNumber` | `int?` | `null` | Minimum row number (inclusive) |
| `ToRowNumber` | `int?` | `null` | Maximum row number (inclusive) |
| `Page` | `int` | `1` | Page number |
| `PageSize` | `int` | `100` | Page size (max 1000) |
| `SortBy` | `string` | `"RowNumber"` | Sort field |
| `SortDescending` | `bool` | `false` | Sort direction |

## Dashboard

The Dashboard shows two sections for each operation:

1. **Row Errors** -- Shows all `BulkRowRecord` entries with errors (`ErrorType` set). Displays row number, error type, error message, and row data (if tracked).

2. **Row Status** -- Shows the aggregated row-level view: one row per file row, with current step, state, completed/total steps, and expandable step-level detail.

Both sections use the same underlying `BulkRowRecord` data, queried with different filters.

```
GET /api/bulks/{id}/errors?errorType=Validation&page=1&pageSize=50
GET /api/bulks/{id}/rows?state=Failed&page=1&pageSize=100
```

## Storage

Row records are stored via `IBulkRowRecordRepository`. Available implementations:

| Provider | Registration | Notes |
|----------|-------------|-------|
| In-Memory | `ms.UseInMemory()` | Default. Lost on restart. |
| Entity Framework | `AddBulkSharpEntityFramework<T>()` | SQL Server with indexes on OperationId, (OperationId, RowNumber, StepIndex) unique, SignalKey, State, and (OperationId, ErrorType). |

### Storage Sizing

Each `BulkRowRecord` without `RowData` is ~150 bytes. With `TrackRowData = true`, add the serialized row size.

For a simple operation (1 validation record + 1 step record per row):

| Rows | Without Data | With Data (avg 200 bytes/row) |
|------|-------------|-------------------------------|
| 10,000 | ~3 MB | ~5 MB |
| 100,000 | ~30 MB | ~50 MB |
| 1,000,000 | ~300 MB | ~500 MB |

For pipeline operations, multiply by the number of steps per row.
