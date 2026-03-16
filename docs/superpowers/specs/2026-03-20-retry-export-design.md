# Retry Failed Rows & Export Rows by Query — Design Spec

**Date**: 2026-03-20
**Status**: Approved

---

## Overview

Two new features for BulkSharp:

1. **Retry failed rows** — re-process single, selected, or all failed rows without re-uploading. Retries resume from the failing step, not from the beginning. Operations and steps declare retryability. Full error history is preserved.

2. **Export rows by query** — download rows filtered by status/error type as CSV/JSON. Two modes: `Report` (metadata + row data for analysis) and `Data` (clean row data matching original schema for fix-and-reupload). Export formatting is pluggable.

---

## Feature 1: Retry Failed Rows

### Design Approach

Service-layer orchestration (Approach A). A dedicated `IBulkRetryService` prepares the retry (validates eligibility, snapshots errors, resets rows) and submits to the existing `IBulkScheduler`. The existing `BulkOperationProcessor` handles execution in retry mode — no new processing pipeline.

### Core Model Changes

#### New Entity: `BulkRowRetryHistory`

Preserves error state before retry overwrites a row record.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | PK |
| `BulkOperationId` | Guid | FK to operation |
| `RowNumber` | int | Row position |
| `StepIndex` | int | Step that failed |
| `Attempt` | int | Which attempt this snapshot is from (0 = original failure) |
| `ErrorType` | BulkErrorType | Error classification |
| `ErrorMessage` | string? | Error detail |
| `FailedAt` | DateTimeOffset | When the failure occurred |
| `RowData` | string? | Snapshot of row data at time of failure |

#### BulkRowRecord Changes

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `RetryAttempt` | int | 0 | Current attempt number |
| `RetryFromStepIndex` | int? | null | Step index to resume from on retry. Set during retry preparation, cleared after processing. |

New method: `BulkRowRecord.ResetForRetry(int fromStepIndex)` — sets `State = Pending`, increments `RetryAttempt`, stores `RetryFromStepIndex`, clears `ErrorType`, `ErrorMessage`, `CompletedAt`.

#### BulkOperation Changes

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `RetryCount` | int | 0 | How many retries performed |

#### BulkOperationStatus Changes

New enum value: `Retrying` — added between `CompletedWithErrors` and terminal states.

State transitions:
- `CompletedWithErrors -> Retrying -> Completed`
- `CompletedWithErrors -> Retrying -> CompletedWithErrors`

### Retryability Declarations

**Operation-level** — `IBulkOperationBase<TMetadata, TRow>`:

```csharp
bool IsRetryable => false; // default interface implementation, opt-in
```

**Step-level** — `[BulkStep]` attribute:

```csharp
[BulkStep(AllowOperationRetry = true)] // default, steps allow operation-level retry unless marked otherwise
```

Note: This is distinct from the existing `MaxRetries` property on `[BulkStep]`, which controls automatic inline retries during processing. See "Naming" section below.

For `IBulkRowOperation` (single-step), the operation-level `IsRetryable` flag is sufficient.

### Retry Service

**`IBulkRetryService`** (abstraction in Core):

```csharp
Task<RetrySubmission> RetryFailedRowsAsync(Guid operationId, RetryRequest request);
Task<RetrySubmission> RetryRowAsync(Guid operationId, int rowNumber);
Task<RetryEligibility> CanRetryAsync(Guid operationId);
```

**`RetryRequest`**:
- `RowNumbers` (IReadOnlyList<int>?) — specific rows, or null for all failed
- `CancellationToken`

**`RetrySubmission`** — returned immediately after the retry is prepared and submitted to the scheduler. Execution is async (same as initial operation creation). Callers poll via `GetBulkOperationStatusAsync` for results.
- `OperationId` (Guid)
- `RowsSubmitted` (int) — how many rows were reset and submitted for retry
- `RowsSkipped` (int) — how many rows were skipped (non-retryable step)
- `SkippedReasons` (IReadOnlyList<string>?) — why specific rows were skipped

**`RetryEligibility`**:
- `IsEligible` (bool)
- `Reason` (string?) — why not eligible

Ineligibility reasons:
- Operation does not exist
- Operation is not in `CompletedWithErrors` state
- Operation's `IsRetryable` is false
- `TrackRowData` is not enabled
- No failed rows exist
- Operation is currently running/retrying

**Concurrency guard**: The status transition from `CompletedWithErrors → Retrying` uses optimistic concurrency via the existing `RowVersion` column on `BulkOperation`. If a concurrent retry wins, the loser gets a concurrency exception and returns an appropriate error.

### Retry Flow

**Phase 1 — Preparation (synchronous, in `IBulkRetryService`)**:

1. **Validate**: operation exists, is `CompletedWithErrors`, `IsRetryable = true`, `TrackRowData = true`
2. **Query failed rows**: all or by specified row numbers (State = `Failed` or `TimedOut`)
3. **For each row**: find the first failed step record (lowest `StepIndex` with failed state)
4. **Validate step retryability**: check `[BulkStep(AllowOperationRetry)]` (see naming note below). Non-retryable rows are skipped and reported in `RetrySubmission.SkippedReasons`
5. **Snapshot**: write failed record state to `BulkRowRetryHistory` for each retryable row
6. **Transition operation**: `CompletedWithErrors -> Retrying` with optimistic concurrency check on `RowVersion`. Increment `RetryCount`
7. **Reset row records**: for each retryable failed row, update the existing `BulkRowRecord` in place — set `State = Pending`, increment `RetryAttempt`, clear `ErrorType`, `ErrorMessage`, `CompletedAt`. The record identity (same PK) is preserved.
8. **Submit to scheduler**: `IBulkScheduler.ScheduleAsync(operationId)` — same dispatch mechanism as initial processing
9. **Return `RetrySubmission`**: immediately, with count of submitted/skipped rows

**Phase 2 — Execution (async, in `BulkOperationProcessor`)**:

The processor loads the operation and checks `Status == Retrying`. This is the branch point:

10. **Processor guards**: `BulkOperationProcessor.ProcessOperationAsync` must allow `Retrying` through its existing guards. Specifically:
    - The terminal-state guard must NOT reject `Retrying`
    - The already-running guard must NOT reject `Retrying`
    - A new branch: if `Status == Retrying`, dispatch to retry processing path in `TypedBulkOperationProcessor`

11. **Retry processing path** (in `TypedBulkOperationProcessor`):
    - **No file re-read**: skip `IManagedStorageProvider.RetrieveFileAsync`
    - **No validation pass**: skip `ValidateMetadataAsync` and row validation
    - **No `MarkValidating()`**: operation stays in `Retrying` state, transitions directly to `Running` via `MarkRunning()`. **Note**: `MarkRunning()` must be updated to accept `Retrying` as a valid source state (currently only allows `Pending` and `Validating`)
    - **Load retry rows**: query `BulkRowRecord` where `State = Pending` and `RetryAttempt > 0` for this operation
    - **Deserialize**: for each row, deserialize `RowData` (JSON) back to `TRow`
    - **Determine start step**: each row's failed `StepIndex` is stored before reset. The retry service stores it in the `BulkRowRetryHistory.StepIndex`. The processor reads the latest history entry for each row to determine the resume step. Alternatively, add a `RetryFromStepIndex` field to `BulkRowRecord` (set during reset, cleared after processing). **Decision: add `RetryFromStepIndex` (int?, nullable) to `BulkRowRecord`** — simpler, avoids a history lookup during hot-path execution.
    - **Execute from step**: for pipeline operations, skip steps 0...(RetryFromStepIndex-1), execute from `RetryFromStepIndex` forward. The step executor **reuses the existing `BulkRowRecord`** for the retry step (updates it in place) rather than calling `CreateStep()`. A new method `BulkRowRecord.ResetForRetry(stepIndex)` handles this.
    - **For `IBulkRowOperation`** (single-step): same flow but always step index 0
    - **`RetryFromStepIndex` clearing**: cleared per-row immediately after that row's retry execution completes (success or failure). This ensures that if the processor crashes mid-retry, remaining unprocessed rows still have `RetryFromStepIndex` set and can be retried again. `ResetForRetry()` always sets it fresh, so stale values from a previous crash are harmless.

12. **Counter reconciliation**: after all retry rows complete, recalculate `SuccessfulRows`, `FailedRows`, and `ProcessedRows` from actual row record states across the entire operation (not just retried rows). `ProcessedRows` should equal `TotalRows` after a complete retry pass. Add `BulkOperation.RecalculateCounters(int successCount, int failCount, int processedCount)` method.

13. **Transition to terminal state**: `Completed` (all rows successful) or `CompletedWithErrors` (some still failed)

### Validation-Failed Rows

Rows that failed during validation have `StepIndex = -1`. These are **not eligible for retry** — validation failures indicate bad input data that requires human correction. The retry service explicitly excludes rows where the only failed record has `StepIndex == -1`. These rows should be exported (Data mode), fixed, and re-uploaded as a new operation.

### Naming: `AllowOperationRetry` vs `MaxRetries`

The existing `[BulkStep]` attribute has a `MaxRetries` property for inline per-step retries (automatic retry with backoff within a single processing run). The new property is named `AllowOperationRetry` (default `true`) to clearly distinguish it from the existing mechanism:
- `MaxRetries` = automatic inline retry during processing (existing)
- `AllowOperationRetry` = whether this step can be retried via the operation-level retry feature (new)

### Max Retry Attempts

A configurable `MaxRetryAttempts` (int, default 10) is added to `BulkSharpOptions`. The retry service checks `operation.RetryCount < options.MaxRetryAttempts` during eligibility validation. This prevents infinite retry loops in automated scenarios.

### Discovery Model

`BulkOperationInfo` (the discovery/registration model) gets a new `IsRetryable` (bool) property, populated during operation discovery from the operation type's `IsRetryable` property. The retry service resolves retryability from `BulkOperationInfo` rather than instantiating the operation.

### Step Retryability Resolution

The retry service needs to check `AllowOperationRetry` per step. Two sources of steps exist:

1. **Attribute-discovered steps** (`[BulkStep]` methods): `AllowOperationRetry` is read directly from the attribute during operation discovery. `BulkOperationInfo` gets a new `StepRetryability` dictionary (`Dictionary<string, bool>`) mapping step name to `AllowOperationRetry` value, populated at discovery time.

2. **Explicit `GetSteps()` steps**: `IBulkStep<TMetadata, TRow>` gets a new property `bool AllowOperationRetry => true;` (default interface implementation). `DelegateStep` exposes it as a constructor parameter (default `true`). This is also captured in `BulkOperationInfo.StepRetryability` during discovery.

The retry service reads `BulkOperationInfo.StepRetryability[stepName]` — no runtime reflection or operation instantiation needed.

### Row Record Query for Retry

`BulkRowRecordQuery` gets a new optional filter: `MinRetryAttempt` (int?). When set, only rows with `RetryAttempt >= MinRetryAttempt` are returned. The processor uses this to load retry-targeted rows:

```csharp
query.State = RowRecordState.Pending;
query.MinRetryAttempt = 1;
```

Both InMemory and EF repositories implement this filter.

### Retry History Repository

**`IBulkRowRetryHistoryRepository`** (abstraction in Core):

```csharp
Task CreateBatchAsync(IEnumerable<BulkRowRetryHistory> records);
Task<PagedResult<BulkRowRetryHistory>> QueryAsync(BulkRowRetryHistoryQuery query);
```

**`BulkRowRetryHistoryQuery`**:
- `BulkOperationId` (Guid)
- `RowNumber` (int?)
- `StepIndex` (int?)
- `Attempt` (int?)
- `Page`, `PageSize`

Implementations:
- `InMemoryBulkRowRetryHistoryRepository` in Processing
- `EntityFrameworkBulkRowRetryHistoryRepository` in Data.EntityFramework

---

## Feature 2: Export Rows by Query

### Design Approach

BulkSharp owns data retrieval and row assembly. Serialization is delegated to a pluggable `IBulkExportFormatter`. A default implementation is provided using CsvHelper/System.Text.Json.

### Export Service

**`IBulkExportService`** (abstraction in Core):

```csharp
Task<ExportResult> ExportAsync(Guid operationId, ExportRequest request);
```

**`ExportRequest`**:
- `Mode` (ExportMode): `Report` | `Data`
- `Format` (ExportFormat): `Csv` | `Json`
- `State` (RowRecordState?) — filter by status
- `ErrorType` (BulkErrorType?) — filter by error type
- `StepName` (string?) — filter by step
- `RowNumbers` (IReadOnlyList<int>?) — specific rows

**`ExportResult`**:
- `Stream` (Stream)
- `ContentType` (string)
- `FileName` (string) — suggested filename
- `RowCount` (int)

### Export Modes

**`Data` mode** — clean row data matching original file schema:
- Requires `TrackRowData = true`
- Deserializes `RowData` from each matching row record
- Serializes to requested format (CSV/JSON)
- Output is ready for fix-and-reupload without sanitization

**`Report` mode** — metadata + row data for analysis:
- Works regardless of `TrackRowData` setting
- Fixed columns: RowNumber, RowId, Status, StepName, StepIndex, ErrorType, ErrorMessage, RetryAttempt, CreatedAt, CompletedAt
- Row data columns appended if `TrackRowData` is enabled
- For auditing, sharing with stakeholders, debugging

### Row Selection Logic

For rows with multiple step records, export the **latest step** (highest `StepIndex`) — represents current state of the row.

### Pluggable Export Formatter

**`IBulkExportFormatter`** (abstraction in Core):

```csharp
public interface IBulkExportFormatter
{
    Task<Stream> FormatReportAsync(IAsyncEnumerable<BulkExportRow> rows, ExportRequest request);
    Task<Stream> FormatDataAsync(IAsyncEnumerable<BulkExportRow> rows, ExportRequest request);
}
```

**`BulkExportRow`** — the payload BulkSharp hands to the formatter:
- `RowNumber` (int)
- `RowId` (string?)
- `State` (RowRecordState)
- `StepName` (string?)
- `StepIndex` (int)
- `ErrorType` (BulkErrorType)
- `ErrorMessage` (string?)
- `RetryAttempt` (int)
- `CreatedAt` (DateTimeOffset)
- `CompletedAt` (DateTimeOffset?)
- `RowData` (string?) — raw JSON
- `RowType` (Type) — for deserialization

**Default implementation**: `DefaultBulkExportFormatter` in Processing, using CsvHelper for CSV and System.Text.Json for JSON.

**Registration via builder**:

```csharp
services.AddBulkSharp(builder => builder
    .UseExportFormatter<MyCompanyExportFormatter>());
```

### Validation

- Operation must exist
- `Mode.Data` requires `TrackRowData = true` — returns error otherwise
- No matching rows returns empty stream with `RowCount = 0` (not an error)

### Stream Lifecycle

`ExportResult.Stream` is a `MemoryStream` owned by the caller — caller is responsible for disposal. For typical bulk operations (thousands to low tens-of-thousands of rows), `MemoryStream` is adequate. The stream is seekable, allowing the dashboard API to set `Content-Length` on HTTP responses.

For future consideration: if operations reach millions of rows, the export service could be extended to write to a temp file or use `IFileStorageProvider`, but this is not in scope for v1.

### Export Pagination Strategy

The `IBulkExportFormatter` receives `IAsyncEnumerable<BulkExportRow>`. The export service internally iterates through `IBulkRowRecordRepository.QueryAsync` page by page (using the existing `Page`/`PageSize` on `BulkRowRecordQuery`) and yields rows as they are loaded. This avoids loading all rows into memory at once while providing a clean streaming interface to the formatter. Page size of 500 is used internally for export iteration.

### RowType Resolution

The export service depends on `IBulkOperationDiscovery` to resolve the `BulkOperationInfo` for the given operation. `BulkOperationInfo.RowType` provides the Type needed for `BulkExportRow.RowType`, which the formatter uses for deserialization of `RowData` JSON into typed objects (for dynamic column extraction in CSV, etc.).

### TrackRowData Interaction

`TrackRowData` on the `[BulkOperation]` attribute is the authoritative flag that controls whether `RowData` is serialized during processing. The existing `BulkSharpOptions.IncludeRowDataInErrors` is a separate, older setting. For retry and export features, only `TrackRowData` is checked — it must be `true` for retry eligibility and Data-mode export. If there is inconsistency between the two settings, `TrackRowData` wins.

---

## Dashboard & API Changes

### New API Endpoints

```
POST /api/bulks/{id}/retry                — retry all failed rows
POST /api/bulks/{id}/retry/rows           — retry specific rows (body: { rowNumbers: [1,3,5] })
GET  /api/bulks/{id}/retry/eligibility    — check if retryable
GET  /api/bulks/{id}/retry/history        — query retry history
GET  /api/bulks/{id}/export?mode=report|data&format=csv|json&state=...&errorType=...
```

### Dashboard UI Additions

- **OperationDetails page**: "Retry Failed Rows" button (visible when `CompletedWithErrors` and `IsRetryable`). Eligibility status. Progress indicator during retry.
- **Error rows list**: Checkbox selection + "Retry Selected" button. Per-row retry button.
- **Export controls**: Format dropdown (CSV/JSON), mode toggle (Report/Data), filter summary, download button. Available on rows list and error list views.
- **Retry history panel**: Expandable per-row history showing previous attempts and errors.
- **State badge**: New `Retrying` status badge.

### IBulkOperationService Facade Additions

```csharp
Task<RetrySubmission> RetryFailedRowsAsync(Guid operationId, RetryRequest request);
Task<RetrySubmission> RetryRowAsync(Guid operationId, int rowNumber);
Task<RetryEligibility> CanRetryAsync(Guid operationId);
Task<ExportResult> ExportAsync(Guid operationId, ExportRequest request);
Task<PagedResult<BulkRowRetryHistory>> QueryRetryHistoryAsync(BulkRowRetryHistoryQuery query);
```

These delegate to `IBulkRetryService` and `IBulkExportService` internally.

---

## EF Core & Storage Changes

### New Entity Configuration: `BulkRowRetryHistory`

- Table: `BulkRowRetryHistory`
- PK: `Id` (Guid)
- Indexes:
  - `(BulkOperationId, RowNumber, StepIndex, Attempt)` — unique, primary lookup
  - `(BulkOperationId)` — filter all history for an operation
  - `(BulkOperationId, RowNumber)` — all retries for a specific row

### BulkRowRecord Changes

- New column: `RetryAttempt` (int, default 0, not null)
- New column: `RetryFromStepIndex` (int?, nullable) — set during retry preparation, cleared after processing

### BulkOperation Changes

- New column: `RetryCount` (int, default 0, not null)

### BulkSharpDbContext

- Add `DbSet<BulkRowRetryHistory>`
- Configure entity in `OnModelCreating` with indexes
- Column types: `ErrorMessage` as `nvarchar(max)`, `RowData` as `nvarchar(max)`, `ErrorType` as int

### Repository Additions

- `IBulkRowRetryHistoryRepository` — interface in Core
- `InMemoryBulkRowRetryHistoryRepository` — in Processing
- `EntityFrameworkBulkRowRetryHistoryRepository` — in Data.EntityFramework

---

## Documentation

### New Documents

- **`docs/guides/state-machine.md`** — full lifecycle of a complex multi-step async pipeline operation. Covers creation, validation, processing, step execution (sync/async/signal), completion, retry. Mermaid diagrams for operation-level and row-level state machines. Narrative walkthrough of a complete scenario with retries.
- **`docs/guides/retry.md`** — dedicated retry feature guide. Covers retryability declarations, retry flow, eligibility, history, API usage.
- **`docs/guides/export.md`** — dedicated export feature guide. Covers modes, formats, filtering, custom formatters, builder registration.

### Updated Documents

- `docs/guides/getting-started.md` — mention retry and export capabilities
- `docs/guides/operations.md` — add `IsRetryable` to operation definition examples
- `docs/guides/pipeline-operations.md` — add `[BulkStep(IsRetryable = false)]` examples, step-level retry semantics
- `docs/guides/dashboard.md` — document retry/export UI and API endpoints
- `docs/guides/configuration.md` — document `UseExportFormatter<T>()` builder extension, `MaxRetryAttempts` option

---

## Samples

- Add `IsRetryable => true` to sample operations
- Add `[BulkStep(IsRetryable = false)]` example on a step that shouldn't retry
- End-to-end retry workflow sample
- Export usage sample
- Custom `IBulkExportFormatter` registration example

---

## Test Coverage

### Unit Tests

- `BulkRowRetryHistory` entity creation and snapshots
- `BulkRowRecord.ResetForRetry()` — state reset, RetryAttempt increment, RetryFromStepIndex set
- `RetryEligibility` — all rejection reasons (not retryable, still running, no failed rows, TrackRowData disabled, non-retryable step, max retry attempts exceeded, validation-failed rows excluded)
- `RetryRequest` / `ExportRequest` validation
- Export row assembly — latest step selection, data mode vs report mode
- Default export formatter — CSV and JSON output for both modes
- State transitions: `CompletedWithErrors -> Retrying -> Running -> Completed`, `CompletedWithErrors -> Retrying -> Running -> CompletedWithErrors`
- Counter recalculation after retry
- Concurrency guard — concurrent retry attempts, RowVersion conflict

### Integration Tests

- Full retry flow: create operation, process with failures, retry failed rows, verify success
- Partial retry: retry specific rows only
- Retry from mid-step: pipeline operation fails at step 2, retry resumes at step 2
- Non-retryable step (`AllowOperationRetry = false`) blocks retry for affected rows
- Validation-failed rows (`StepIndex == -1`) excluded from retry
- Retry history preserved across multiple retry attempts
- RetryFromStepIndex cleared after processing
- Max retry attempts enforced
- Export report mode with failed rows
- Export data mode — verify output matches original file schema
- Export with filters (state, error type)
- Export with `TrackRowData = false` — report mode works, data mode returns error
- Export stream is seekable and disposable

### Architecture Tests

- New services follow existing patterns (interface in Core, impl in Processing)
- New repository follows existing patterns

### Dashboard Tests

- Retry API endpoints return correct responses
- Export API endpoints stream correct content types
- Eligibility endpoint reflects operation state
