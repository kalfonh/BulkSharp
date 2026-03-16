# Retry Failed Rows & Export Rows by Query — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add in-place retry of failed rows (resuming from the failing step) and export rows by query (report and data modes) to BulkSharp.

**Architecture:** Service-layer orchestration. `IBulkRetryService` prepares retry (validates, snapshots errors, resets rows) and submits to the existing `IBulkScheduler`. `BulkOperationProcessor` detects `Retrying` state and enters a retry-specific code path. `IBulkExportService` queries rows and delegates formatting to a pluggable `IBulkExportFormatter`. Both services are exposed through `IBulkOperationService` facade and Dashboard API endpoints.

**Tech Stack:** .NET 8, EF Core, CsvHelper, System.Text.Json, xUnit, Blazor Server

**Spec:** `docs/superpowers/specs/2026-03-20-retry-export-design.md`

---

## File Map

### New Files

**Core Abstractions & Models:**
- `src/BulkSharp.Core/Domain/Operations/BulkRowRetryHistory.cs` — retry error history entity
- `src/BulkSharp.Core/Domain/Queries/BulkRowRetryHistoryQuery.cs` — query model for retry history
- `src/BulkSharp.Core/Domain/Retry/RetryRequest.cs` — retry request model
- `src/BulkSharp.Core/Domain/Retry/RetrySubmission.cs` — retry submission result
- `src/BulkSharp.Core/Domain/Retry/RetryEligibility.cs` — eligibility check result
- `src/BulkSharp.Core/Domain/Export/ExportRequest.cs` — export request model
- `src/BulkSharp.Core/Domain/Export/ExportResult.cs` — export result model
- `src/BulkSharp.Core/Domain/Export/ExportMode.cs` — enum: Report, Data
- `src/BulkSharp.Core/Domain/Export/ExportFormat.cs` — enum: Csv, Json
- `src/BulkSharp.Core/Domain/Export/BulkExportRow.cs` — export row payload
- `src/BulkSharp.Core/Abstractions/Operations/IBulkRetryService.cs` — retry service interface
- `src/BulkSharp.Core/Abstractions/Operations/IBulkExportService.cs` — export service interface
- `src/BulkSharp.Core/Abstractions/Export/IBulkExportFormatter.cs` — pluggable export formatter
- `src/BulkSharp.Core/Abstractions/Storage/IBulkRowRetryHistoryRepository.cs` — retry history repository

**Processing Implementations:**
- `src/BulkSharp.Processing/Services/BulkRetryService.cs` — retry service implementation
- `src/BulkSharp.Processing/Services/BulkExportService.cs` — export service implementation
- `src/BulkSharp.Processing/Export/DefaultBulkExportFormatter.cs` — default CSV/JSON formatter
- `src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRetryHistoryRepository.cs` — in-memory retry history

**EF Core:**
- `src/BulkSharp.Data.EntityFramework/EntityFrameworkBulkRowRetryHistoryRepository.cs` — EF retry history

**Tests:**
- `tests/BulkSharp.UnitTests/Retry/RetryEligibilityTests.cs`
- `tests/BulkSharp.UnitTests/Retry/BulkRowRecordResetTests.cs`
- `tests/BulkSharp.UnitTests/Retry/BulkRowRetryHistoryTests.cs`
- `tests/BulkSharp.UnitTests/Export/ExportServiceTests.cs`
- `tests/BulkSharp.UnitTests/Export/DefaultBulkExportFormatterTests.cs`
- `tests/BulkSharp.IntegrationTests/RetryIntegrationTests.cs`
- `tests/BulkSharp.IntegrationTests/ExportIntegrationTests.cs`

**Documentation:**
- `docs/guides/state-machine.md`
- `docs/guides/retry.md`
- `docs/guides/export.md`

### Modified Files

**Core:**
- `src/BulkSharp.Core/Domain/Operations/BulkOperationStatus.cs` — add `Retrying`
- `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs` — add `RetryCount`, `MarkRetrying()`, `RecalculateCounters()`, update `MarkRunning()` and `MarkFailed()`
- `src/BulkSharp.Core/Domain/Operations/BulkRowRecord.cs` — add `RetryAttempt`, `RetryFromStepIndex`, `ResetForRetry()`
- `src/BulkSharp.Core/Domain/Queries/BulkRowRecordQuery.cs` — add `MinRetryAttempt`
- `src/BulkSharp.Core/Domain/Discovery/BulkOperationInfo.cs` — add `IsRetryable`, `StepRetryability`
- `src/BulkSharp.Core/Abstractions/Operations/IBulkOperationBase.cs` — add `IsRetryable` default property
- `src/BulkSharp.Core/Abstractions/Operations/IBulkStep.cs` — add `AllowOperationRetry` default property
- `src/BulkSharp.Core/Abstractions/Operations/IBulkOperationService.cs` — add retry/export methods
- `src/BulkSharp.Core/Attributes/BulkStepAttribute.cs` — add `AllowOperationRetry`
- `src/BulkSharp.Core/Steps/DelegateStep.cs` — add `AllowOperationRetry` constructor param
- `src/BulkSharp.Core/Configuration/BulkSharpOptions.cs` — add `MaxRetryAttempts`
- `src/BulkSharp/Builders/BulkSharpBuilder.cs` — add `UseExportFormatter<T>()`

**Processing:**
- `src/BulkSharp.Processing/Processors/BulkOperationProcessor.cs` — allow `Retrying` through guards
- `src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs` — add retry processing path
- `src/BulkSharp.Processing/Services/BulkOperationService.cs` — delegate retry/export to services
- `src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRecordRepository.cs` — add `MinRetryAttempt` filter

**EF Core:**
- `src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs` — add `BulkRowRetryHistory` entity config
- `src/BulkSharp.Data.EntityFramework/EntityFrameworkBulkRowRecordRepository.cs` — add `MinRetryAttempt` filter

**Dashboard:**
- `src/BulkSharp.Dashboard/WebApplicationExtensions.cs` — add retry/export endpoints

**Discovery (step retryability capture):**
- `src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs` — pass `AllowOperationRetry` to `DelegateStep` during discovery

**Logging:**
- `src/BulkSharp.Processing/Logging/LogMessages.Processing.cs` — add retry log messages

**Architecture Tests:**
- `tests/BulkSharp.ArchitectureTests/ConventionTests.cs` — verify new services follow patterns

---

## Task 1: Core Model Changes — BulkOperationStatus, BulkRowRecord, BulkOperation

**Files:**
- Modify: `src/BulkSharp.Core/Domain/Operations/BulkOperationStatus.cs`
- Modify: `src/BulkSharp.Core/Domain/Operations/BulkRowRecord.cs`
- Modify: `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs`
- Test: `tests/BulkSharp.UnitTests/Retry/BulkRowRecordResetTests.cs`

- [ ] **Step 1: Write failing tests for BulkRowRecord.ResetForRetry()**

```csharp
// tests/BulkSharp.UnitTests/Retry/BulkRowRecordResetTests.cs
using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.UnitTests.Retry;

public class BulkRowRecordResetTests
{
    [Fact]
    public void ResetForRetry_ShouldSetStateToPending()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Equal(RowRecordState.Pending, record.State);
    }

    [Fact]
    public void ResetForRetry_ShouldIncrementRetryAttempt()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Equal(1, record.RetryAttempt);
    }

    [Fact]
    public void ResetForRetry_ShouldSetRetryFromStepIndex()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Equal(2, record.RetryFromStepIndex);
    }

    [Fact]
    public void ResetForRetry_ShouldClearErrorFields()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("some error", BulkErrorType.Processing);

        record.ResetForRetry(2);

        Assert.Null(record.ErrorType);
        Assert.Null(record.ErrorMessage);
        Assert.Null(record.CompletedAt);
    }

    [Fact]
    public void ResetForRetry_CalledTwice_ShouldIncrementRetryAttemptEachTime()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, "row1", "step1", 2);
        record.MarkFailed("first error", BulkErrorType.Processing);
        record.ResetForRetry(2);

        record.MarkFailed("second error", BulkErrorType.Processing);
        record.ResetForRetry(2);

        Assert.Equal(2, record.RetryAttempt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~BulkRowRecordResetTests" --no-build 2>&1 || true`
Expected: Build error — `ResetForRetry`, `RetryAttempt`, `RetryFromStepIndex` don't exist yet.

- [ ] **Step 3: Add `Retrying` to BulkOperationStatus**

```csharp
// src/BulkSharp.Core/Domain/Operations/BulkOperationStatus.cs
public enum BulkOperationStatus
{
    Pending,
    Validating,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled,
    Retrying
}
```

- [ ] **Step 4: Add retry fields to BulkRowRecord and implement ResetForRetry()**

Add to `src/BulkSharp.Core/Domain/Operations/BulkRowRecord.cs`:

```csharp
public int RetryAttempt { get; set; }
public int? RetryFromStepIndex { get; set; }

public void ResetForRetry(int fromStepIndex)
{
    State = RowRecordState.Pending;
    RetryAttempt++;
    RetryFromStepIndex = fromStepIndex;
    ErrorType = null;
    ErrorMessage = null;
    CompletedAt = null;
}
```

- [ ] **Step 5: Add retry fields and methods to BulkOperation**

Add to `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs`:

New property:
```csharp
public int RetryCount { get; set; }
```

New method:
```csharp
/// <summary>Transitions the operation to Retrying state. Only valid from CompletedWithErrors.</summary>
public void MarkRetrying()
{
    if (Status != BulkOperationStatus.CompletedWithErrors)
        throw new InvalidOperationException($"Cannot transition from {Status} to Retrying");
    Status = BulkOperationStatus.Retrying;
    RetryCount++;
    CompletedAt = null;
}
```

Update `MarkRunning()` to accept `Retrying`:
```csharp
public void MarkRunning()
{
    if (Status is not (BulkOperationStatus.Pending or BulkOperationStatus.Validating or BulkOperationStatus.Retrying))
        throw new InvalidOperationException($"Cannot transition from {Status} to Running");
    Status = BulkOperationStatus.Running;
    if (StartedAt == null)
        StartedAt = DateTime.UtcNow;
}
```

Update `MarkFailed()` to also recognize `Retrying` as non-terminal:
```csharp
public void MarkFailed(string errorMessage)
{
    if (Status is BulkOperationStatus.Completed or BulkOperationStatus.CompletedWithErrors
        or BulkOperationStatus.Failed or BulkOperationStatus.Cancelled)
        return;
    Status = BulkOperationStatus.Failed;
    ErrorMessage = errorMessage;
    CompletedAt = DateTime.UtcNow;
}
```
(No change needed — `Retrying` is not in the terminal list, so `MarkFailed` already works correctly for `Retrying`.)

New method for counter recalculation:
```csharp
/// <summary>Recalculates row counters from actual row record states. Used after retry.</summary>
public void RecalculateCounters(int successCount, int failCount, int processedCount)
{
    Interlocked.Exchange(ref _successfulRows, successCount);
    Interlocked.Exchange(ref _failedRows, failCount);
    Interlocked.Exchange(ref _processedRows, processedCount);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~BulkRowRecordResetTests" -v minimal`
Expected: All 5 tests PASS.

- [ ] **Step 7: Run full test suite to verify no regressions**

Run: `dotnet test --filter "Category!=E2E" -v minimal`
Expected: All existing tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/BulkSharp.Core/Domain/Operations/BulkOperationStatus.cs src/BulkSharp.Core/Domain/Operations/BulkRowRecord.cs src/BulkSharp.Core/Domain/Operations/BulkOperation.cs tests/BulkSharp.UnitTests/Retry/BulkRowRecordResetTests.cs
git commit -m "Add Retrying status, retry fields on BulkRowRecord and BulkOperation"
```

---

## Task 2: Retryability Declarations — Attributes, Interfaces, Discovery

**Files:**
- Modify: `src/BulkSharp.Core/Abstractions/Operations/IBulkOperationBase.cs`
- Modify: `src/BulkSharp.Core/Abstractions/Operations/IBulkStep.cs`
- Modify: `src/BulkSharp.Core/Attributes/BulkStepAttribute.cs`
- Modify: `src/BulkSharp.Core/Steps/DelegateStep.cs`
- Modify: `src/BulkSharp.Core/Domain/Discovery/BulkOperationInfo.cs`
- Modify: `src/BulkSharp.Core/Configuration/BulkSharpOptions.cs`

- [ ] **Step 1: Add `IsRetryable` to `IBulkOperationBase`**

```csharp
// src/BulkSharp.Core/Abstractions/Operations/IBulkOperationBase.cs
public interface IBulkOperationBase<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    bool IsRetryable => false;
    Task ValidateMetadataAsync(TMetadata metadata, CancellationToken cancellationToken = default);
    Task ValidateRowAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Add `AllowOperationRetry` to `IBulkStep` and `BulkStepAttribute`**

```csharp
// src/BulkSharp.Core/Abstractions/Operations/IBulkStep.cs
public interface IBulkStep<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    string Name { get; }
    int MaxRetries { get; }
    bool AllowOperationRetry => true;
    Task ExecuteAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);
}
```

```csharp
// src/BulkSharp.Core/Attributes/BulkStepAttribute.cs — add property:
/// <summary>Whether this step can be retried via the operation-level retry feature. Default: true.</summary>
public bool AllowOperationRetry { get; set; } = true;
```

- [ ] **Step 3: Add `AllowOperationRetry` to `DelegateStep`**

```csharp
// src/BulkSharp.Core/Steps/DelegateStep.cs
public sealed class DelegateStep<TMetadata, TRow> : IBulkStep<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    private readonly Func<TRow, TMetadata, CancellationToken, Task> _execute;

    internal DelegateStep(string name, Func<TRow, TMetadata, CancellationToken, Task> execute, int maxRetries = 0, bool allowOperationRetry = true)
    {
        Name = name;
        _execute = execute;
        MaxRetries = maxRetries;
        AllowOperationRetry = allowOperationRetry;
    }

    public string Name { get; }
    public int MaxRetries { get; }
    public bool AllowOperationRetry { get; }

    public Task ExecuteAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default) =>
        _execute(row, metadata, cancellationToken);
}
```

- [ ] **Step 4: Add retryability to `BulkOperationInfo`**

```csharp
// src/BulkSharp.Core/Domain/Discovery/BulkOperationInfo.cs — add:
public bool IsRetryable { get; init; }
public IReadOnlyDictionary<string, bool> StepRetryability { get; init; } = new Dictionary<string, bool>();
```

- [ ] **Step 5: Add `MaxRetryAttempts` to `BulkSharpOptions`**

```csharp
// src/BulkSharp.Core/Configuration/BulkSharpOptions.cs — add:
/// <summary>
/// Maximum number of operation-level retry attempts allowed. Default: 10.
/// Set to 0 to disable the limit.
/// </summary>
public int MaxRetryAttempts { get; set; } = 10;
```

- [ ] **Step 6: Update `DiscoverStepsFromAttributes` to pass `AllowOperationRetry`**

In `src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs`, update the `DiscoverStepsFromAttributes` method to pass `AllowOperationRetry` from the attribute to `DelegateStep`:

```csharp
.Select(x =>
{
    var method = x.Method;
    return (IBulkStep<TStepMeta, TStepRow>)new DelegateStep<TStepMeta, TStepRow>(
        x.Attr!.Name,
        (row, meta, ct) => (Task)method.Invoke(operationInstance, [row, meta, ct])!,
        x.Attr!.MaxRetries,
        x.Attr!.AllowOperationRetry);
})
```

- [ ] **Step 7: Run full test suite**

Run: `dotnet test --filter "Category!=E2E" -v minimal`
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/BulkSharp.Core/Abstractions/Operations/IBulkOperationBase.cs src/BulkSharp.Core/Abstractions/Operations/IBulkStep.cs src/BulkSharp.Core/Attributes/BulkStepAttribute.cs src/BulkSharp.Core/Steps/DelegateStep.cs src/BulkSharp.Core/Domain/Discovery/BulkOperationInfo.cs src/BulkSharp.Core/Configuration/BulkSharpOptions.cs src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs
git commit -m "Add retryability declarations to operations, steps, and discovery model"
```

---

## Task 3: BulkRowRetryHistory Entity & Repository

**Files:**
- Create: `src/BulkSharp.Core/Domain/Operations/BulkRowRetryHistory.cs`
- Create: `src/BulkSharp.Core/Domain/Queries/BulkRowRetryHistoryQuery.cs`
- Create: `src/BulkSharp.Core/Abstractions/Storage/IBulkRowRetryHistoryRepository.cs`
- Create: `src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRetryHistoryRepository.cs`
- Test: `tests/BulkSharp.UnitTests/Retry/BulkRowRetryHistoryTests.cs`

- [ ] **Step 1: Write failing tests for BulkRowRetryHistory entity**

```csharp
// tests/BulkSharp.UnitTests/Retry/BulkRowRetryHistoryTests.cs
using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.UnitTests.Retry;

public class BulkRowRetryHistoryTests
{
    [Fact]
    public void CreateFromRecord_ShouldSnapshotFailedState()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 5, "row5", "ValidateAddress", 2);
        record.MarkFailed("Invalid zip code", BulkErrorType.Validation);
        record.RowData = """{"name":"John","zip":"00000"}""";

        var history = BulkRowRetryHistory.CreateFromRecord(record);

        Assert.Equal(record.BulkOperationId, history.BulkOperationId);
        Assert.Equal(5, history.RowNumber);
        Assert.Equal(2, history.StepIndex);
        Assert.Equal(0, history.Attempt);
        Assert.Equal(BulkErrorType.Validation, history.ErrorType);
        Assert.Equal("Invalid zip code", history.ErrorMessage);
        Assert.Equal(record.RowData, history.RowData);
        Assert.NotNull(history.FailedAt);
    }

    [Fact]
    public void CreateFromRecord_WithRetryAttempt_ShouldUseRecordRetryAttempt()
    {
        var record = BulkRowRecord.CreateStep(Guid.NewGuid(), 1, null, "step1", 0);
        record.MarkFailed("error", BulkErrorType.Processing);
        record.ResetForRetry(0);
        record.MarkFailed("error again", BulkErrorType.Processing);

        var history = BulkRowRetryHistory.CreateFromRecord(record);

        Assert.Equal(1, history.Attempt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~BulkRowRetryHistoryTests" --no-build 2>&1 || true`
Expected: Build error — `BulkRowRetryHistory` doesn't exist yet.

- [ ] **Step 3: Create BulkRowRetryHistory entity**

```csharp
// src/BulkSharp.Core/Domain/Operations/BulkRowRetryHistory.cs
namespace BulkSharp.Core.Domain.Operations;

public sealed class BulkRowRetryHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BulkOperationId { get; set; }
    public int RowNumber { get; set; }
    public int StepIndex { get; set; }
    public int Attempt { get; set; }
    public BulkErrorType? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FailedAt { get; set; }
    public string? RowData { get; set; }

    public static BulkRowRetryHistory CreateFromRecord(BulkRowRecord record) => new()
    {
        BulkOperationId = record.BulkOperationId,
        RowNumber = record.RowNumber,
        StepIndex = record.StepIndex,
        Attempt = record.RetryAttempt,
        ErrorType = record.ErrorType,
        ErrorMessage = record.ErrorMessage,
        FailedAt = record.CompletedAt ?? DateTime.UtcNow,
        RowData = record.RowData
    };
}
```

- [ ] **Step 4: Create BulkRowRetryHistoryQuery**

```csharp
// src/BulkSharp.Core/Domain/Queries/BulkRowRetryHistoryQuery.cs
namespace BulkSharp.Core.Domain.Queries;

public sealed class BulkRowRetryHistoryQuery
{
    public required Guid OperationId { get; init; }
    public int? RowNumber { get; set; }
    public int? StepIndex { get; set; }
    public int? Attempt { get; set; }

    private int _page = 1;
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = 100;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 1000);
    }
}
```

- [ ] **Step 5: Create IBulkRowRetryHistoryRepository**

```csharp
// src/BulkSharp.Core/Abstractions/Storage/IBulkRowRetryHistoryRepository.cs
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Core.Abstractions.Storage;

public interface IBulkRowRetryHistoryRepository
{
    Task CreateBatchAsync(IEnumerable<BulkRowRetryHistory> records, CancellationToken ct = default);
    Task<PagedResult<BulkRowRetryHistory>> QueryAsync(BulkRowRetryHistoryQuery query, CancellationToken ct = default);
}
```

- [ ] **Step 6: Create InMemoryBulkRowRetryHistoryRepository**

```csharp
// src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRetryHistoryRepository.cs
using System.Collections.Concurrent;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Processing.Storage.InMemory;

internal sealed class InMemoryBulkRowRetryHistoryRepository : IBulkRowRetryHistoryRepository
{
    private readonly ConcurrentDictionary<Guid, BulkRowRetryHistory> _store = new();

    public Task CreateBatchAsync(IEnumerable<BulkRowRetryHistory> records, CancellationToken ct = default)
    {
        foreach (var record in records)
            _store[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<PagedResult<BulkRowRetryHistory>> QueryAsync(BulkRowRetryHistoryQuery query, CancellationToken ct = default)
    {
        var q = _store.Values.Where(r => r.BulkOperationId == query.OperationId);

        if (query.RowNumber.HasValue)
            q = q.Where(r => r.RowNumber == query.RowNumber.Value);
        if (query.StepIndex.HasValue)
            q = q.Where(r => r.StepIndex == query.StepIndex.Value);
        if (query.Attempt.HasValue)
            q = q.Where(r => r.Attempt == query.Attempt.Value);

        var all = q.OrderBy(r => r.RowNumber).ThenBy(r => r.Attempt).ToList();
        var items = all.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        return Task.FromResult(new PagedResult<BulkRowRetryHistory>
        {
            Items = items,
            TotalCount = all.Count,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~BulkRowRetryHistoryTests" -v minimal`
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/BulkSharp.Core/Domain/Operations/BulkRowRetryHistory.cs src/BulkSharp.Core/Domain/Queries/BulkRowRetryHistoryQuery.cs src/BulkSharp.Core/Abstractions/Storage/IBulkRowRetryHistoryRepository.cs src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRetryHistoryRepository.cs tests/BulkSharp.UnitTests/Retry/BulkRowRetryHistoryTests.cs
git commit -m "Add BulkRowRetryHistory entity, query, repository interface and in-memory impl"
```

---

## Task 4: BulkRowRecordQuery MinRetryAttempt Filter

**Files:**
- Modify: `src/BulkSharp.Core/Domain/Queries/BulkRowRecordQuery.cs`
- Modify: `src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRecordRepository.cs`
- Modify: `src/BulkSharp.Data.EntityFramework/EntityFrameworkBulkRowRecordRepository.cs`

- [ ] **Step 1: Add `MinRetryAttempt` to BulkRowRecordQuery**

```csharp
// src/BulkSharp.Core/Domain/Queries/BulkRowRecordQuery.cs — add property:
public int? MinRetryAttempt { get; set; }
```

- [ ] **Step 2: Add filter to InMemoryBulkRowRecordRepository.QueryAsync()**

In `src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRecordRepository.cs`, after the `ToRowNumber` filter block, add:

```csharp
if (query.MinRetryAttempt.HasValue)
    q = q.Where(r => r.RetryAttempt >= query.MinRetryAttempt.Value);
```

- [ ] **Step 3: Add filter to EntityFrameworkBulkRowRecordRepository.QueryAsync()**

Find the query building section in `src/BulkSharp.Data.EntityFramework/EntityFrameworkBulkRowRecordRepository.cs` and add the same filter pattern used for other nullable int filters:

```csharp
if (query.MinRetryAttempt.HasValue)
    q = q.Where(r => r.RetryAttempt >= query.MinRetryAttempt.Value);
```

- [ ] **Step 3b: CRITICAL — Update EF `UpdateAsync` and `UpdateBatchAsync` to include retry fields**

The EF `UpdateAsync` uses `ExecuteUpdateAsync` with an explicit property list. The new `RetryAttempt`, `RetryFromStepIndex`, and `RowData` fields are NOT included — they will be silently dropped on save. Add them:

In `UpdateAsync`:
```csharp
.SetProperty(r => r.RetryAttempt, record.RetryAttempt)
.SetProperty(r => r.RetryFromStepIndex, record.RetryFromStepIndex)
.SetProperty(r => r.RowData, record.RowData)
```

In `UpdateBatchAsync` (the `Attach`/`IsModified` block), add:
```csharp
entry.Property(r => r.RetryAttempt).IsModified = true;
entry.Property(r => r.RetryFromStepIndex).IsModified = true;
entry.Property(r => r.RowData).IsModified = true;
```

- [ ] **Step 4: Run full test suite**

Run: `dotnet test --filter "Category!=E2E" -v minimal`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BulkSharp.Core/Domain/Queries/BulkRowRecordQuery.cs src/BulkSharp.Processing/Storage/InMemory/InMemoryBulkRowRecordRepository.cs src/BulkSharp.Data.EntityFramework/EntityFrameworkBulkRowRecordRepository.cs
git commit -m "Add MinRetryAttempt filter to BulkRowRecordQuery and repository implementations"
```

---

## Task 5: Retry Service — Models and Interface

**Files:**
- Create: `src/BulkSharp.Core/Domain/Retry/RetryRequest.cs`
- Create: `src/BulkSharp.Core/Domain/Retry/RetrySubmission.cs`
- Create: `src/BulkSharp.Core/Domain/Retry/RetryEligibility.cs`
- Create: `src/BulkSharp.Core/Abstractions/Operations/IBulkRetryService.cs`
- Test: `tests/BulkSharp.UnitTests/Retry/RetryEligibilityTests.cs`

- [ ] **Step 1: Create retry models**

```csharp
// src/BulkSharp.Core/Domain/Retry/RetryRequest.cs
namespace BulkSharp.Core.Domain.Retry;

public sealed class RetryRequest
{
    public IReadOnlyList<int>? RowNumbers { get; init; }
}
```

```csharp
// src/BulkSharp.Core/Domain/Retry/RetrySubmission.cs
namespace BulkSharp.Core.Domain.Retry;

public sealed class RetrySubmission
{
    public Guid OperationId { get; init; }
    public int RowsSubmitted { get; init; }
    public int RowsSkipped { get; init; }
    public IReadOnlyList<string>? SkippedReasons { get; init; }
}
```

```csharp
// src/BulkSharp.Core/Domain/Retry/RetryEligibility.cs
namespace BulkSharp.Core.Domain.Retry;

public sealed class RetryEligibility
{
    public bool IsEligible { get; init; }
    public string? Reason { get; init; }

    public static RetryEligibility Eligible() => new() { IsEligible = true };
    public static RetryEligibility Ineligible(string reason) => new() { IsEligible = false, Reason = reason };
}
```

- [ ] **Step 2: Create IBulkRetryService**

```csharp
// src/BulkSharp.Core/Abstractions/Operations/IBulkRetryService.cs
using BulkSharp.Core.Domain.Retry;

namespace BulkSharp.Core.Abstractions.Operations;

public interface IBulkRetryService
{
    Task<RetrySubmission> RetryFailedRowsAsync(Guid operationId, RetryRequest request, CancellationToken cancellationToken = default);
    Task<RetrySubmission> RetryRowAsync(Guid operationId, int rowNumber, CancellationToken cancellationToken = default);
    Task<RetryEligibility> CanRetryAsync(Guid operationId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Verify build succeeds**

Run: `dotnet build src/BulkSharp.Core -v minimal`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/BulkSharp.Core/Domain/Retry/ src/BulkSharp.Core/Abstractions/Operations/IBulkRetryService.cs
git commit -m "Add retry service interface and request/result models"
```

---

## Task 6: Retry Service — Implementation

**Files:**
- Create: `src/BulkSharp.Processing/Services/BulkRetryService.cs`
- Test: `tests/BulkSharp.UnitTests/Retry/RetryEligibilityTests.cs`

- [ ] **Step 1: Write failing eligibility tests**

```csharp
// tests/BulkSharp.UnitTests/Retry/RetryEligibilityTests.cs
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Domain.Retry;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.Options;

namespace BulkSharp.UnitTests.Retry;

public class RetryEligibilityTests
{
    // These tests exercise CanRetryAsync() on the BulkRetryService.
    // The service needs: operationRepository, rowRecordRepository, retryHistoryRepository,
    // operationDiscovery, scheduler, options.
    // We'll use InMemory implementations + Moq for discovery/scheduler.

    [Fact]
    public async Task CanRetry_OperationNotFound_ReturnsIneligible()
    {
        var (service, opRepo, _, _, _) = CreateService();

        var result = await service.CanRetryAsync(Guid.NewGuid());

        Assert.False(result.IsEligible);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanRetry_OperationStillRunning_ReturnsIneligible()
    {
        var (service, opRepo, _, _, _) = CreateService();
        var op = CreateOperation(BulkOperationStatus.Running, "test-op");
        await opRepo.CreateAsync(op);

        var result = await service.CanRetryAsync(op.Id);

        Assert.False(result.IsEligible);
        Assert.Contains("Running", result.Reason);
    }

    [Fact]
    public async Task CanRetry_OperationNotRetryable_ReturnsIneligible()
    {
        var (service, opRepo, _, _, discovery) = CreateService(isRetryable: false);
        var op = CreateOperation(BulkOperationStatus.CompletedWithErrors, "test-op");
        await opRepo.CreateAsync(op);

        var result = await service.CanRetryAsync(op.Id);

        Assert.False(result.IsEligible);
        Assert.Contains("not retryable", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanRetry_TrackRowDataDisabled_ReturnsIneligible()
    {
        var (service, opRepo, _, _, _) = CreateService(trackRowData: false);
        var op = CreateOperation(BulkOperationStatus.CompletedWithErrors, "test-op");
        await opRepo.CreateAsync(op);

        var result = await service.CanRetryAsync(op.Id);

        Assert.False(result.IsEligible);
        Assert.Contains("TrackRowData", result.Reason);
    }

    [Fact]
    public async Task CanRetry_MaxRetryAttemptsExceeded_ReturnsIneligible()
    {
        var (service, opRepo, _, _, _) = CreateService(maxRetryAttempts: 2);
        var op = CreateOperation(BulkOperationStatus.CompletedWithErrors, "test-op");
        op.RetryCount = 2;
        await opRepo.CreateAsync(op);

        var result = await service.CanRetryAsync(op.Id);

        Assert.False(result.IsEligible);
        Assert.Contains("maximum", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanRetry_ValidOperation_ReturnsEligible()
    {
        var (service, opRepo, rowRepo, _, _) = CreateService();
        var op = CreateOperation(BulkOperationStatus.CompletedWithErrors, "test-op");
        await opRepo.CreateAsync(op);
        // Add a failed row so there's something to retry
        var record = BulkRowRecord.CreateStep(op.Id, 1, null, "step1", 0);
        record.MarkFailed("error", BulkErrorType.Processing);
        record.RowData = """{"name":"test"}""";
        await rowRepo.CreateAsync(record);

        var result = await service.CanRetryAsync(op.Id);

        Assert.True(result.IsEligible);
    }

    // Helper methods — implementation depends on how BulkRetryService is constructed.
    // The actual helper code will be written during implementation, matching the DI pattern.
    // For now these are pseudocode stubs that the implementer fills in.

    private static (IBulkRetryService service, IBulkOperationRepository opRepo,
        IBulkRowRecordRepository rowRepo, IBulkRowRetryHistoryRepository historyRepo,
        IBulkOperationDiscovery discovery) CreateService(
            bool isRetryable = true, bool trackRowData = true, int maxRetryAttempts = 10)
    {
        // Use InMemory repositories from Processing project
        // Mock IBulkOperationDiscovery to return a BulkOperationInfo with IsRetryable/TrackRowData
        // Mock IBulkScheduler (not needed for eligibility checks)
        // Create BulkRetryService with these dependencies
        throw new NotImplementedException("Fill in during implementation");
    }

    private static BulkOperation CreateOperation(BulkOperationStatus status, string name)
    {
        var op = new BulkOperation { OperationName = name, Status = status };
        // For CompletedWithErrors, set it properly via state machine
        // Since we can't easily transition through the state machine in tests,
        // set Status directly (it has a public setter for serialization)
        return op;
    }
}
```

Note to implementer: The `CreateService` helper needs concrete wiring. Use `InMemoryBulkOperationRepository`, `InMemoryBulkRowRecordRepository`, `InMemoryBulkRowRetryHistoryRepository`, a mock `IBulkOperationDiscovery` that returns `BulkOperationInfo` with the given flags, a mock `IBulkScheduler`, and `Options.Create(new BulkSharpOptions { MaxRetryAttempts = maxRetryAttempts })`.

- [ ] **Step 2: Implement BulkRetryService**

Create `src/BulkSharp.Processing/Services/BulkRetryService.cs`. The service depends on:
- `IBulkOperationRepository` — load/update operation
- `IBulkRowRecordRepository` — query/update row records
- `IBulkRowRetryHistoryRepository` — snapshot errors
- `IBulkOperationDiscovery` — check retryability, step retryability
- `IBulkScheduler` — submit retry to scheduler
- `IOptions<BulkSharpOptions>` — max retry attempts
- `ILogger<BulkRetryService>`

Key logic for `CanRetryAsync`:
1. Load operation — not found → ineligible
2. Check status is `CompletedWithErrors` — otherwise ineligible with status name
3. Get `BulkOperationInfo` → check `IsRetryable`
4. Check `TrackRowData`
5. Check `RetryCount < MaxRetryAttempts` (if MaxRetryAttempts > 0)
6. Query failed rows (State=Failed or TimedOut, StepIndex >= 0) — none → ineligible
7. Return eligible

Key logic for `RetryFailedRowsAsync`:
1. Call `CanRetryAsync` — if ineligible, throw
2. Query failed rows (all or filtered by request.RowNumbers)
3. Exclude StepIndex == -1 (validation failures)
4. For each row, check step retryability via `BulkOperationInfo.StepRetryability` — skip non-retryable steps, track reasons
5. Snapshot: create `BulkRowRetryHistory` for each retryable failed row
6. `operation.MarkRetrying()` — uses optimistic concurrency via RowVersion
7. Save operation
8. Reset row records: `record.ResetForRetry(failedStepIndex)`
9. Update rows in batch
10. Snapshot history in batch
11. Schedule: `scheduler.ScheduleBulkOperationAsync(operationId)`
12. Return `RetrySubmission`

`RetryRowAsync` delegates to `RetryFailedRowsAsync` with a single-item `RowNumbers`.

- [ ] **Step 3: Wire up test helpers and run eligibility tests**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~RetryEligibilityTests" -v minimal`
Expected: All 6 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/BulkSharp.Processing/Services/BulkRetryService.cs tests/BulkSharp.UnitTests/Retry/RetryEligibilityTests.cs
git commit -m "Implement BulkRetryService with eligibility checking and retry preparation"
```

---

## Task 7: Processor Retry Path

**Files:**
- Modify: `src/BulkSharp.Processing/Processors/BulkOperationProcessor.cs`
- Modify: `src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs`
- Modify: `src/BulkSharp.Processing/Logging/LogMessages.Processing.cs`

- [ ] **Step 1: Update BulkOperationProcessor guards**

In `src/BulkSharp.Processing/Processors/BulkOperationProcessor.cs`, modify `ProcessOperationAsync`:

Terminal state guard (line ~37): add `Retrying` to the allowed-through list. Change:
```csharp
if (operation.Status is BulkOperationStatus.Completed or BulkOperationStatus.CompletedWithErrors or BulkOperationStatus.Failed or BulkOperationStatus.Cancelled)
```
Keep this guard as-is — `Retrying` is NOT in this list, so it passes through.

Already-running guard (line ~44): `Retrying` should pass through. Change:
```csharp
if (operation.Status is BulkOperationStatus.Running or BulkOperationStatus.Validating)
```
Keep as-is — `Retrying` is NOT in this list either.

BUT: after the guards, line ~58 calls `operation.MarkValidating()` which will throw for `Retrying` status. Add a branch BEFORE `MarkValidating()`:

```csharp
if (operation.Status == BulkOperationStatus.Retrying)
{
    // Retry mode: skip validation, go directly to typed processor retry path
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["OperationId"] = operationId,
        ["OperationName"] = operation.OperationName
    });
    await ProcessOperationWithOperation(operation, opInfo, cancellationToken).ConfigureAwait(false);
    operation.MarkCompleted();
    return; // finally block handles persistence and events
}
```

Wait — the `finally` block is outside. We need to restructure. Actually looking at the code more carefully, the try/catch/finally already wraps everything after the guards. We just need to branch the `MarkValidating()` call. Change the flow after `opInfo` resolution:

```csharp
if (operation.Status != BulkOperationStatus.Retrying)
{
    operation.MarkValidating();
    await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
    // ... status changed event dispatch ...
}
```

The rest of the try block (`ProcessOperationWithOperation` + `MarkCompleted()`) stays the same.

- [ ] **Step 2: Add retry processing path to TypedBulkOperationProcessor**

In `src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs`, modify `ProcessOperationAsync`:

At the top, add a branch for retry mode:

```csharp
public async Task ProcessOperationAsync(
    BulkOperation operation,
    T operationInstance,
    TMetadata metadata,
    CancellationToken cancellationToken = default)
{
    if (operation.Status == BulkOperationStatus.Retrying)
    {
        await ProcessRetryAsync(operation, operationInstance, metadata, cancellationToken).ConfigureAwait(false);
        return;
    }

    // ... existing code unchanged ...
}
```

Add the new `ProcessRetryAsync` method:

```csharp
private async Task ProcessRetryAsync(
    BulkOperation operation,
    T operationInstance,
    TMetadata metadata,
    CancellationToken cancellationToken)
{
    var opInfo = operationDiscovery.GetOperation(operation.OperationName);

    // Transition to Running
    operation.MarkRunning();
    await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
    logger.TransitioningToProcessing(operation.Id);

    // Load retry-targeted rows
    var retryRows = new List<BulkRowRecord>();
    var page = 1;
    while (true)
    {
        var result = await rowRecordRepository.QueryAsync(new BulkRowRecordQuery
        {
            OperationId = operation.Id,
            State = RowRecordState.Pending,
            MinRetryAttempt = 1,
            Page = page,
            PageSize = 500
        }, cancellationToken).ConfigureAwait(false);

        retryRows.AddRange(result.Items);
        if (!result.HasNextPage) break;
        page++;
    }

    if (retryRows.Count == 0)
    {
        logger.LogInformation("No retry rows found for operation {OperationId}", operation.Id);
        return;
    }

    // Build step list (same as normal processing)
    List<IBulkStep<TMetadata, TRow>>? steps = null;
    Func<TRow, TMetadata, int, CancellationToken, Task>? rowDelegate = null;

    if (operationInstance is IBulkPipelineOperation<TMetadata, TRow> pipeline)
    {
        var explicitSteps = pipeline.GetSteps().ToList();
        var discoveredSteps = DiscoverStepsFromAttributes<TMetadata, TRow>(pipeline.GetType(), pipeline);
        var explicitNames = new HashSet<string>(explicitSteps.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        steps = new List<IBulkStep<TMetadata, TRow>>(explicitSteps);
        steps.AddRange(discoveredSteps.Where(s => !explicitNames.Contains(s.Name)));
    }
    else
    {
        var rowOp = (IBulkRowOperation<TMetadata, TRow>)operationInstance;
        var stepName = opInfo?.DefaultStepName ?? operation.OperationName;
        var rowProcessorArray = rowProcessors.ToArray();

        rowDelegate = async (row, meta, rowNumber, token) =>
        {
            // Reuse existing record — find it and update
            var record = await rowRecordRepository.GetByOperationRowStepAsync(
                operation.Id, rowNumber, 0, token).ConfigureAwait(false);
            if (record != null)
            {
                record.MarkRunning();
                rowRecordFlushService.TrackUpdate(record);
            }

            try
            {
                await rowOp.ProcessRowAsync(row, meta, token).ConfigureAwait(false);
                foreach (var rp in rowProcessorArray)
                    await rp.ProcessAsync(row, meta, token).ConfigureAwait(false);
                record?.MarkCompleted();
            }
            catch
            {
                record?.MarkFailed("Processing failed", BulkErrorType.Processing);
                throw;
            }
            finally
            {
                if (record != null) rowRecordFlushService.TrackUpdate(record);
            }
        };
    }

    // Process each retry row
    foreach (var rowRecord in retryRows)
    {
        var row = JsonSerializer.Deserialize<TRow>(rowRecord.RowData!, BulkSharpJsonDefaults.Options)!;

        try
        {
            if (steps != null)
            {
                // Pipeline: resume from RetryFromStepIndex
                var startStep = rowRecord.RetryFromStepIndex ?? 0;
                for (int i = startStep; i < steps.Count; i++)
                {
                    await stepExecutor.ExecuteStepAsync(steps[i], row, metadata, operation.Id, rowRecord.RowNumber, i, cancellationToken)
                        .ConfigureAwait(false);
                }
                operation.RecordRowResult(success: true);
            }
            else
            {
                await rowDelegate!(row, metadata, rowRecord.RowNumber, cancellationToken).ConfigureAwait(false);
                operation.RecordRowResult(success: true);
            }
        }
        catch (Exception ex)
        {
            operation.RecordRowResult(success: false);
            logger.LogWarning(ex, "Retry failed for row {RowNumber} in operation {OperationId}", rowRecord.RowNumber, operation.Id);
        }
        finally
        {
            rowRecord.RetryFromStepIndex = null;
            rowRecordFlushService.TrackUpdate(rowRecord);
        }
    }

    await rowRecordFlushService.FlushAsync(cancellationToken).ConfigureAwait(false);

    // Recalculate counters from all row records
    var allRows = new List<BulkRowRecord>();
    page = 1;
    while (true)
    {
        var result = await rowRecordRepository.QueryAsync(new BulkRowRecordQuery
        {
            OperationId = operation.Id,
            Page = page,
            PageSize = 1000
        }, cancellationToken).ConfigureAwait(false);
        allRows.AddRange(result.Items);
        if (!result.HasNextPage) break;
        page++;
    }

    // Only count execution step records (StepIndex >= 0), and for each row take the highest StepIndex
    var latestPerRow = allRows
        .Where(r => r.StepIndex >= 0)
        .GroupBy(r => r.RowNumber)
        .Select(g => g.OrderByDescending(r => r.StepIndex).First())
        .ToList();

    var successCount = latestPerRow.Count(r => r.State == RowRecordState.Completed);
    var failCount = latestPerRow.Count(r => r.State is RowRecordState.Failed or RowRecordState.TimedOut);
    operation.RecalculateCounters(successCount, failCount, operation.TotalRows);
}
```

Note: The `RecordRowResult` calls during retry use Interlocked increments. Then `RecalculateCounters` overwrites with correct values at the end. This is safe because retry runs single-operation and the counters are recalculated atomically after all rows complete.

- [ ] **Step 3: Add retry log messages**

In `src/BulkSharp.Processing/Logging/LogMessages.Processing.cs`, add:

```csharp
[LoggerMessage(EventId = 120, Level = LogLevel.Information, Message = "Starting retry processing for operation {OperationId} with {RowCount} rows")]
public static partial void RetryProcessingStarted(this ILogger logger, Guid operationId, int rowCount);

[LoggerMessage(EventId = 121, Level = LogLevel.Information, Message = "Retry processing completed for operation {OperationId}: {SuccessCount} succeeded, {FailCount} failed")]
public static partial void RetryProcessingCompleted(this ILogger logger, Guid operationId, int successCount, int failCount);
```

- [ ] **Step 4: Run full test suite**

Run: `dotnet test --filter "Category!=E2E" -v minimal`
Expected: All tests PASS. (The retry path isn't exercised yet by existing tests, but nothing should break.)

- [ ] **Step 5: Commit**

```bash
git add src/BulkSharp.Processing/Processors/BulkOperationProcessor.cs src/BulkSharp.Processing/Processors/TypedBulkOperationProcessor.cs src/BulkSharp.Processing/Logging/LogMessages.Processing.cs
git commit -m "Add retry processing path to BulkOperationProcessor and TypedBulkOperationProcessor"
```

---

## Task 8: Retry Integration Tests

**Files:**
- Create: `tests/BulkSharp.IntegrationTests/RetryIntegrationTests.cs`

- [ ] **Step 1: Write integration tests**

These tests exercise the full retry flow: create operation → process → retry failed rows → verify. Use the existing `EndToEndTests` pattern as reference (InMemory storage + Immediate scheduler).

The test needs a pipeline operation with `IsRetryable => true` and `TrackRowData = true` where some rows fail deterministically (e.g., row with specific name throws on first attempt but succeeds on second via a static counter).

Key tests:
1. Full retry flow — create, process with failures, retry all failed, verify CompletedWithErrors → Retrying → Completed
2. Partial retry — retry specific row numbers only
3. Retry from mid-step — pipeline fails at step 2, retry resumes at step 2
4. Non-retryable step — step with `AllowOperationRetry = false` skips retry
5. Validation-failed rows excluded from retry
6. Retry history preserved
7. Max retry attempts enforced
8. RetryFromStepIndex cleared after processing

The implementer should create a `RetryableTestOperation` in the test project that implements `IBulkPipelineOperation` with `IsRetryable => true`, has steps that can be made to fail/succeed via test hooks (static dictionary or similar).

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/BulkSharp.IntegrationTests --filter "FullyQualifiedName~RetryIntegrationTests" -v minimal`
Expected: All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/BulkSharp.IntegrationTests/RetryIntegrationTests.cs
git commit -m "Add retry integration tests covering full flow, partial retry, mid-step resume"
```

---

## Task 9: Export Models, Interface, and Formatter

**Files:**
- Create: `src/BulkSharp.Core/Domain/Export/ExportMode.cs`
- Create: `src/BulkSharp.Core/Domain/Export/ExportFormat.cs`
- Create: `src/BulkSharp.Core/Domain/Export/ExportRequest.cs`
- Create: `src/BulkSharp.Core/Domain/Export/ExportResult.cs`
- Create: `src/BulkSharp.Core/Domain/Export/BulkExportRow.cs`
- Create: `src/BulkSharp.Core/Abstractions/Export/IBulkExportFormatter.cs`
- Create: `src/BulkSharp.Core/Abstractions/Operations/IBulkExportService.cs`
- Create: `src/BulkSharp.Processing/Export/DefaultBulkExportFormatter.cs`
- Test: `tests/BulkSharp.UnitTests/Export/DefaultBulkExportFormatterTests.cs`

- [ ] **Step 1: Create export enums and models**

```csharp
// src/BulkSharp.Core/Domain/Export/ExportMode.cs
namespace BulkSharp.Core.Domain.Export;
public enum ExportMode { Report, Data }
```

```csharp
// src/BulkSharp.Core/Domain/Export/ExportFormat.cs
namespace BulkSharp.Core.Domain.Export;
public enum ExportFormat { Csv, Json }
```

```csharp
// src/BulkSharp.Core/Domain/Export/ExportRequest.cs
using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Export;

public sealed class ExportRequest
{
    public ExportMode Mode { get; init; } = ExportMode.Report;
    public ExportFormat Format { get; init; } = ExportFormat.Csv;
    public RowRecordState? State { get; init; }
    public BulkErrorType? ErrorType { get; init; }
    public string? StepName { get; init; }
    public IReadOnlyList<int>? RowNumbers { get; init; }
}
```

```csharp
// src/BulkSharp.Core/Domain/Export/ExportResult.cs
namespace BulkSharp.Core.Domain.Export;

public sealed class ExportResult
{
    public required Stream Stream { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public int RowCount { get; init; }
}
```

```csharp
// src/BulkSharp.Core/Domain/Export/BulkExportRow.cs
using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Export;

public sealed class BulkExportRow
{
    public int RowNumber { get; init; }
    public string? RowId { get; init; }
    public RowRecordState State { get; init; }
    public string? StepName { get; init; }
    public int StepIndex { get; init; }
    public BulkErrorType? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryAttempt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? RowData { get; init; }
    public Type? RowType { get; init; }
}
```

- [ ] **Step 2: Create IBulkExportFormatter and IBulkExportService**

```csharp
// src/BulkSharp.Core/Abstractions/Export/IBulkExportFormatter.cs
using BulkSharp.Core.Domain.Export;

namespace BulkSharp.Core.Abstractions.Export;

public interface IBulkExportFormatter
{
    Task<Stream> FormatReportAsync(IAsyncEnumerable<BulkExportRow> rows, ExportRequest request, CancellationToken ct = default);
    Task<Stream> FormatDataAsync(IAsyncEnumerable<BulkExportRow> rows, ExportRequest request, CancellationToken ct = default);
}
```

```csharp
// src/BulkSharp.Core/Abstractions/Operations/IBulkExportService.cs
using BulkSharp.Core.Domain.Export;

namespace BulkSharp.Core.Abstractions.Operations;

public interface IBulkExportService
{
    Task<ExportResult> ExportAsync(Guid operationId, ExportRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing tests for DefaultBulkExportFormatter**

```csharp
// tests/BulkSharp.UnitTests/Export/DefaultBulkExportFormatterTests.cs
using BulkSharp.Core.Domain.Export;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Export;

namespace BulkSharp.UnitTests.Export;

public class DefaultBulkExportFormatterTests
{
    [Fact]
    public async Task FormatReportAsync_Csv_ShouldIncludeMetadataColumns()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatReportAsync(rows,
            new ExportRequest { Format = ExportFormat.Csv, Mode = ExportMode.Report });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        Assert.Contains("RowNumber", content);
        Assert.Contains("ErrorMessage", content);
        Assert.Contains("Failed", content);
    }

    [Fact]
    public async Task FormatDataAsync_Csv_ShouldOnlyIncludeRowDataColumns()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatDataAsync(rows,
            new ExportRequest { Format = ExportFormat.Csv, Mode = ExportMode.Data });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        // Should NOT have metadata columns
        Assert.DoesNotContain("ErrorMessage", content);
        // Should have row data
        Assert.Contains("John", content);
    }

    [Fact]
    public async Task FormatReportAsync_Json_ShouldProduceValidJson()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatReportAsync(rows,
            new ExportRequest { Format = ExportFormat.Json, Mode = ExportMode.Report });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        var doc = System.Text.Json.JsonDocument.Parse(content);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task FormatDataAsync_Json_ShouldOnlyIncludeRowData()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatDataAsync(rows,
            new ExportRequest { Format = ExportFormat.Json, Mode = ExportMode.Data });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        Assert.DoesNotContain("ErrorMessage", content);
        Assert.Contains("John", content);
    }

    private static async IAsyncEnumerable<BulkExportRow> CreateTestRows()
    {
        yield return new BulkExportRow
        {
            RowNumber = 1,
            RowId = "r1",
            State = RowRecordState.Failed,
            StepName = "Validate",
            StepIndex = 0,
            ErrorType = BulkErrorType.Validation,
            ErrorMessage = "Invalid email",
            RetryAttempt = 0,
            CreatedAt = DateTime.UtcNow,
            RowData = """{"name":"John","email":"bad"}"""
        };
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Implement DefaultBulkExportFormatter**

Create `src/BulkSharp.Processing/Export/DefaultBulkExportFormatter.cs`.

For CSV Report: write header row with metadata columns + dynamic columns from RowData JSON, then data rows.
For CSV Data: parse RowData JSON, extract properties, write as CSV rows.
For JSON Report: serialize array of objects with metadata + RowData merged.
For JSON Data: write array of raw RowData JSON objects.

Use `System.Text.Json.JsonDocument` to parse RowData dynamically. Use `CsvHelper` or manual StringBuilder for CSV.

- [ ] **Step 5: Run formatter tests**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~DefaultBulkExportFormatterTests" -v minimal`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BulkSharp.Core/Domain/Export/ src/BulkSharp.Core/Abstractions/Export/ src/BulkSharp.Core/Abstractions/Operations/IBulkExportService.cs src/BulkSharp.Processing/Export/ tests/BulkSharp.UnitTests/Export/
git commit -m "Add export models, IBulkExportFormatter, IBulkExportService, and default formatter"
```

---

## Task 10: Export Service Implementation

**Files:**
- Create: `src/BulkSharp.Processing/Services/BulkExportService.cs`
- Test: `tests/BulkSharp.UnitTests/Export/ExportServiceTests.cs`

- [ ] **Step 1: Write failing export service tests**

Test scenarios:
- Export report mode returns stream with metadata columns
- Export data mode requires TrackRowData — returns error without it
- Export data mode with TrackRowData returns clean row data
- Export with state filter returns only matching rows
- Export with no matching rows returns empty stream with RowCount=0
- Latest step per row is selected (not all step records)

- [ ] **Step 2: Implement BulkExportService**

Create `src/BulkSharp.Processing/Services/BulkExportService.cs`. Dependencies:
- `IBulkOperationRepository`
- `IBulkRowRecordRepository`
- `IBulkOperationDiscovery`
- `IBulkExportFormatter`

Logic:
1. Load operation — not found → throw
2. Get `BulkOperationInfo` for `RowType` and `TrackRowData`
3. If `Mode == Data` and `!TrackRowData` → throw
4. Page through rows via `QueryAsync`, yield `BulkExportRow` per row (latest StepIndex per RowNumber)
5. Delegate to formatter: `FormatReportAsync` or `FormatDataAsync`
6. Build `ExportResult` with suggested filename

- [ ] **Step 3: Run export tests**

Run: `dotnet test tests/BulkSharp.UnitTests --filter "FullyQualifiedName~ExportServiceTests" -v minimal`
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/BulkSharp.Processing/Services/BulkExportService.cs tests/BulkSharp.UnitTests/Export/ExportServiceTests.cs
git commit -m "Implement BulkExportService with paged row assembly and formatter delegation"
```

---

## Task 11: DI Registration and Builder

**Files:**
- Modify: `src/BulkSharp/Builders/BulkSharpBuilder.cs` (NOTE: builder is in `src/BulkSharp/`, not `src/BulkSharp.Core/`)
- Modify: `src/BulkSharp/ServiceCollectionExtensions.cs` — register new services in `RegisterProcessingServices()` and `RegisterApiServices()`
- Modify: `src/BulkSharp.Core/Builders/MetadataStorageBuilder.cs` — register `InMemoryBulkRowRetryHistoryRepository` in `UseInMemory()`
- Modify: `src/BulkSharp.Core/Abstractions/Operations/IBulkOperationService.cs`
- Modify: `src/BulkSharp.Processing/Services/BulkOperationService.cs`
- Modify: `src/BulkSharp.Core/Steps/Step.cs` — forward `AllowOperationRetry` in `Step.From()`

- [ ] **Step 1: Add `UseExportFormatter<T>()` to BulkSharpBuilder**

```csharp
// src/BulkSharp/Builders/BulkSharpBuilder.cs — add method:
/// <summary>
/// Registers a custom export formatter. If not called, the default CSV/JSON formatter is used.
/// </summary>
public BulkSharpBuilder UseExportFormatter<T>() where T : class, IBulkExportFormatter
{
    _services.AddSingleton<IBulkExportFormatter, T>();
    return this;
}
```

- [ ] **Step 2: Register services in `src/BulkSharp/ServiceCollectionExtensions.cs`**

In `RegisterProcessingServices()`, add:
```csharp
services.AddScoped<IBulkRetryService, BulkRetryService>();
services.AddScoped<IBulkExportService, BulkExportService>();
services.TryAddSingleton<IBulkExportFormatter, DefaultBulkExportFormatter>();
```

In `RegisterApiServices()`, add (export and retry history are useful in API-only mode):
```csharp
services.AddScoped<IBulkRetryService, BulkRetryService>();
services.AddScoped<IBulkExportService, BulkExportService>();
services.TryAddSingleton<IBulkExportFormatter, DefaultBulkExportFormatter>();
```

- [ ] **Step 3: Register `InMemoryBulkRowRetryHistoryRepository` in metadata storage builder**

In `src/BulkSharp.Core/Builders/MetadataStorageBuilder.cs`, inside the `UseInMemory()` method, add alongside existing InMemory registrations:
```csharp
_services.TryAddSingleton<IBulkRowRetryHistoryRepository, InMemoryBulkRowRetryHistoryRepository>();
```

- [ ] **Step 4: Update `Step.From()` to forward `AllowOperationRetry`**

In `src/BulkSharp.Core/Steps/Step.cs`, update `Step.From()`:
```csharp
return new DelegateStep<TMetadata, TRow>(attr.Name, method, attr.MaxRetries, attr.AllowOperationRetry);
```

- [ ] **Step 5: Add retry/export methods to IBulkOperationService and BulkOperationService**

Update `src/BulkSharp.Core/Abstractions/Operations/IBulkOperationService.cs`:
```csharp
Task<RetrySubmission> RetryFailedRowsAsync(Guid operationId, RetryRequest request, CancellationToken cancellationToken = default);
Task<RetrySubmission> RetryRowAsync(Guid operationId, int rowNumber, CancellationToken cancellationToken = default);
Task<RetryEligibility> CanRetryAsync(Guid operationId, CancellationToken cancellationToken = default);
Task<ExportResult> ExportAsync(Guid operationId, ExportRequest request, CancellationToken cancellationToken = default);
Task<PagedResult<BulkRowRetryHistory>> QueryRetryHistoryAsync(BulkRowRetryHistoryQuery query, CancellationToken cancellationToken = default);
```

Update `src/BulkSharp.Processing/Services/BulkOperationService.cs` — add constructor params for `IBulkRetryService`, `IBulkExportService`, `IBulkRowRetryHistoryRepository` and delegate calls.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test --filter "Category!=E2E" -v minimal`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BulkSharp/Builders/BulkSharpBuilder.cs src/BulkSharp/ServiceCollectionExtensions.cs src/BulkSharp.Core/Builders/MetadataStorageBuilder.cs src/BulkSharp.Core/Steps/Step.cs src/BulkSharp.Core/Abstractions/Operations/IBulkOperationService.cs src/BulkSharp.Processing/Services/BulkOperationService.cs
git commit -m "Wire up retry and export services in DI, add to IBulkOperationService facade"
```

---

## Task 12: EF Core Changes

**Files:**
- Modify: `src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs`
- Create: `src/BulkSharp.Data.EntityFramework/EntityFrameworkBulkRowRetryHistoryRepository.cs`

- [ ] **Step 1: Update BulkSharpDbContext**

Add to `BulkSharpDbContext`:
```csharp
public DbSet<BulkRowRetryHistory> BulkRowRetryHistory { get; set; }
```

In `OnModelCreating`, add `BulkRowRetryHistory` configuration:
```csharp
modelBuilder.Entity<BulkRowRetryHistory>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ErrorMessage).HasColumnType("nvarchar(max)");
    entity.Property(e => e.RowData).HasColumnType("nvarchar(max)");
    entity.HasIndex(e => new { e.BulkOperationId, e.RowNumber, e.StepIndex, e.Attempt }).IsUnique();
    entity.HasIndex(e => e.BulkOperationId);
    entity.HasIndex(e => new { e.BulkOperationId, e.RowNumber });
});
```

In the `BulkRowRecord` configuration, add the new columns:
```csharp
// Inside the existing BulkRowRecord entity configuration:
entity.Property(e => e.RetryAttempt).HasDefaultValue(0);
entity.Property(e => e.RetryFromStepIndex);
```

In the `BulkOperation` configuration, add:
```csharp
entity.Property(e => e.RetryCount).HasDefaultValue(0);
```

- [ ] **Step 2: Create EntityFrameworkBulkRowRetryHistoryRepository**

Follow the same pattern as `EntityFrameworkBulkRowRecordRepository` — use `IDbContextFactory<BulkSharpDbContext>`, `AsNoTracking()` for queries, batch create.

- [ ] **Step 3: Register EF repository in MetadataStorageBuilderExtensions**

In `src/BulkSharp.Data.EntityFramework/MetadataStorageBuilderExtensions.cs`, add:
```csharp
services.AddScoped<IBulkRowRetryHistoryRepository, EntityFrameworkBulkRowRetryHistoryRepository>();
```

- [ ] **Step 4: Build EF project**

Run: `dotnet build src/BulkSharp.Data.EntityFramework -v minimal`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/BulkSharp.Data.EntityFramework/
git commit -m "Add BulkRowRetryHistory to DbContext, EF repository, and schema configuration"
```

---

## Task 13: Dashboard API Endpoints

**Files:**
- Modify: `src/BulkSharp.Dashboard/WebApplicationExtensions.cs`

- [ ] **Step 1: Add retry and export endpoints**

Add before the `configureAdditionalEndpoints?.Invoke(app)` line:

```csharp
// Retry endpoints
var retryAllEndpoint = app.MapPost("/api/bulks/{id:guid}/retry", async (
    Guid id,
    [FromServices] IBulkOperationService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.RetryFailedRowsAsync(id, new RetryRequest(), cancellationToken);
    return Results.Ok(result);
});
if (authorizationPolicy != null) retryAllEndpoint.RequireAuthorization(authorizationPolicy);

var retryRowsEndpoint = app.MapPost("/api/bulks/{id:guid}/retry/rows", async (
    Guid id,
    [FromBody] RetryRowsRequest request,
    [FromServices] IBulkOperationService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.RetryFailedRowsAsync(id, new RetryRequest { RowNumbers = request.RowNumbers }, cancellationToken);
    return Results.Ok(result);
});
if (authorizationPolicy != null) retryRowsEndpoint.RequireAuthorization(authorizationPolicy);

app.MapGet("/api/bulks/{id:guid}/retry/eligibility", async (
    Guid id,
    [FromServices] IBulkOperationService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.CanRetryAsync(id, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/bulks/{id:guid}/retry/history", async (
    Guid id,
    [FromServices] IBulkOperationService service,
    [FromQuery] int? rowNumber,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 100,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 200);
    var result = await service.QueryRetryHistoryAsync(new BulkRowRetryHistoryQuery
    {
        OperationId = id,
        RowNumber = rowNumber,
        Page = page,
        PageSize = pageSize
    }, cancellationToken);
    return Results.Ok(result);
});

// Export endpoint
app.MapGet("/api/bulks/{id:guid}/export", async (
    Guid id,
    [FromServices] IBulkOperationService service,
    [FromQuery] string mode = "report",
    [FromQuery] string format = "csv",
    [FromQuery] string? state = null,
    [FromQuery] string? errorType = null,
    [FromQuery] string? stepName = null,
    CancellationToken cancellationToken = default) =>
{
    var exportMode = Enum.TryParse<ExportMode>(mode, true, out var m) ? m : ExportMode.Report;
    var exportFormat = Enum.TryParse<ExportFormat>(format, true, out var f) ? f : ExportFormat.Csv;
    RowRecordState? parsedState = null;
    if (!string.IsNullOrEmpty(state) && Enum.TryParse<RowRecordState>(state, true, out var s))
        parsedState = s;
    BulkErrorType? parsedErrorType = null;
    if (!string.IsNullOrEmpty(errorType) && Enum.TryParse<BulkErrorType>(errorType, true, out var et))
        parsedErrorType = et;

    var result = await service.ExportAsync(id, new ExportRequest
    {
        Mode = exportMode,
        Format = exportFormat,
        State = parsedState,
        ErrorType = parsedErrorType,
        StepName = stepName
    }, cancellationToken);

    return Results.File(result.Stream, result.ContentType, result.FileName);
});
```

Add the request record:
```csharp
internal record RetryRowsRequest(IReadOnlyList<int> RowNumbers);
```

- [ ] **Step 2: Run dashboard tests**

Run: `dotnet test tests/BulkSharp.Dashboard.Tests -v minimal`
Expected: Existing tests PASS.

- [ ] **Step 3: Commit**

```bash
git add src/BulkSharp.Dashboard/WebApplicationExtensions.cs
git commit -m "Add retry and export API endpoints to dashboard"
```

---

## Task 14: Export Integration Tests

**Files:**
- Create: `tests/BulkSharp.IntegrationTests/ExportIntegrationTests.cs`

- [ ] **Step 1: Write integration tests**

Test scenarios:
1. Export report mode CSV — create operation with mixed success/failure, export report, verify CSV contains metadata + row data
2. Export data mode CSV — export failed rows only, verify output matches original schema
3. Export with state filter — only Failed rows returned
4. Export with TrackRowData=false in report mode — works but no row data columns
5. Export JSON format — verify valid JSON array

Use the same test infrastructure as `EndToEndTests` (InMemory + Immediate scheduler).

- [ ] **Step 2: Run export tests**

Run: `dotnet test tests/BulkSharp.IntegrationTests --filter "FullyQualifiedName~ExportIntegrationTests" -v minimal`
Expected: All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/BulkSharp.IntegrationTests/ExportIntegrationTests.cs
git commit -m "Add export integration tests for report/data modes, CSV/JSON, and filtering"
```

---

## Task 15: Architecture Tests

**Files:**
- Modify: `tests/BulkSharp.ArchitectureTests/ConventionTests.cs`

- [ ] **Step 1: Add architecture tests for new services**

Verify that:
- `IBulkRetryService` is in Core, implementation is in Processing
- `IBulkExportService` is in Core, implementation is in Processing
- `IBulkExportFormatter` is in Core, default implementation is in Processing
- `IBulkRowRetryHistoryRepository` is in Core, implementations in Processing and Data.EntityFramework
- New domain models are in Core

- [ ] **Step 2: Run architecture tests**

Run: `dotnet test tests/BulkSharp.ArchitectureTests -v minimal`
Expected: All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/BulkSharp.ArchitectureTests/
git commit -m "Add architecture tests for retry and export service layering"
```

---

## Task 16: Documentation — State Machine Guide

**Files:**
- Create: `docs/guides/state-machine.md`

- [ ] **Step 1: Write comprehensive state machine documentation**

Cover:
1. Operation-level state machine with Mermaid diagram (Pending → Validating → Running → Completed/CompletedWithErrors/Failed/Cancelled, plus CompletedWithErrors → Retrying → Running → ...)
2. Row-level state machine with Mermaid diagram (Pending → Running → Completed/Failed/TimedOut/WaitingForCompletion, retry: Failed → Pending → Running → ...)
3. Full walkthrough of a complex multi-step async pipeline operation:
   - Operation creation and file upload
   - Validation phase (row-by-row)
   - Processing phase (step-by-step execution)
   - Async step completion (signal/polling)
   - Completion with errors
   - Retry preparation (eligibility, snapshot, reset)
   - Retry execution (resume from failed step)
   - Final completion
4. Error history lifecycle
5. Counter behavior during initial processing vs retry

- [ ] **Step 2: Commit**

```bash
git add docs/guides/state-machine.md
git commit -m "Add comprehensive state machine guide with Mermaid diagrams"
```

---

## Task 17: Documentation — Retry and Export Guides

**Files:**
- Create: `docs/guides/retry.md`
- Create: `docs/guides/export.md`

- [ ] **Step 1: Write retry guide**

Cover:
- Enabling retryability (`IsRetryable => true` on operation)
- Step-level `AllowOperationRetry` control
- `TrackRowData` requirement
- API usage (eligibility check, retry all, retry specific rows)
- Retry history querying
- `MaxRetryAttempts` configuration
- Dashboard UI walkthrough

- [ ] **Step 2: Write export guide**

Cover:
- Export modes: Report vs Data
- Export formats: CSV, JSON
- Query filters (state, error type, step)
- Custom `IBulkExportFormatter` implementation and registration
- API endpoint usage
- `TrackRowData` interaction

- [ ] **Step 3: Commit**

```bash
git add docs/guides/retry.md docs/guides/export.md
git commit -m "Add retry and export feature guides"
```

---

## Task 18: Update Existing Documentation

**Files:**
- Modify: `docs/guides/configuration.md`
- Modify: `docs/guides/step-operations.md`
- Modify: `docs/guides/dashboard.md`
- Modify: `docs/guides/error-handling.md`

- [ ] **Step 1: Update configuration guide**

Add `MaxRetryAttempts` to options table and `UseExportFormatter<T>()` to builder API section.

- [ ] **Step 2: Update step operations guide**

Add `AllowOperationRetry` attribute property, `IsRetryable` interface property, and examples.

- [ ] **Step 3: Update dashboard guide**

Add retry and export API endpoints. Document new UI elements.

- [ ] **Step 4: Update error handling guide**

Mention retry as an error recovery strategy. Link to retry guide.

- [ ] **Step 5: Commit**

```bash
git add docs/guides/configuration.md docs/guides/step-operations.md docs/guides/dashboard.md docs/guides/error-handling.md
git commit -m "Update existing guides with retry and export references"
```

---

## Task 19: Sample Project Updates

**Files:**
- Modify sample operations to demonstrate retryability
- Add export formatter example

- [ ] **Step 1: Update sample operations**

In the sample projects, find existing operations and:
- Add `bool IsRetryable => true;` to at least one sample operation
- Add `[BulkStep("StepName", AllowOperationRetry = false)]` example
- Ensure `TrackRowData = true` is set on sample operations that demonstrate retry

- [ ] **Step 2: Run sample project builds**

Run: `dotnet build samples/ -v minimal`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add samples/
git commit -m "Update sample operations with retry and export examples"
```

---

## Task 20: Final Validation

- [ ] **Step 1: Run full test suite**

Run: `dotnet test --filter "Category!=E2E" -v minimal`
Expected: ALL tests PASS (unit + integration + architecture + dashboard).

- [ ] **Step 2: Build all configurations**

Run: `dotnet build -c Debug && dotnet build -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Verify no regressions in existing tests**

Run: `dotnet test --filter "Category!=E2E" --logger "console;verbosity=normal" 2>&1 | tail -20`
Verify test count has increased and all pass.

---

## Out of Scope (Future Tasks)

- **Blazor UI components**: The spec describes Dashboard UI additions (retry button, export controls, retry history panel, Retrying status badge). This plan covers the API endpoints and backend services only. Blazor component work should be a separate plan after the API layer is stable and tested.
- **EF Migrations**: The schema changes are configured in `BulkSharpDbContext.OnModelCreating`. Generating the actual migration (`dotnet ef migrations add ...`) depends on the consumer's database. Document migration requirements in the retry guide instead.
