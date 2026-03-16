# Glossary

## Core Concepts

**Bulk Operation**
A named, trackable unit of work that processes rows from a file (CSV or JSON). Defined by implementing `IBulkRowOperation<TMetadata, TRow>` or `IBulkPipelineOperation<TMetadata, TRow>` and decorating with `[BulkOperation("name")]`.

**Metadata**
A user-defined object (implementing `IBulkMetadata`) that carries context for an operation. Examples: who requested it, target environment, processing options. Validated before processing starts. Serialized as JSON and stored with the operation record.

**Row**
A single data record from the uploaded file (implementing `IBulkRow`). Each row is validated and processed independently. For CSV files, rows map to lines; for JSON, rows map to array elements.

**Step**
An ordered processing stage within a step-based operation. Each step has a name, retry count, and `ExecuteAsync` method. Steps execute sequentially per row. Defined by implementing `IBulkStep<TMetadata, TRow>`.

**Async Step**
A step that completes externally rather than inline. Supports two modes: **Polling** (BulkSharp checks periodically) and **Signal** (external system calls back with a signal key). Defined by implementing `IAsyncBulkStep<TMetadata, TRow>`.

## Operation States

**Pending**
Operation created but not yet picked up by a scheduler worker.

**Processing**
A worker is actively processing rows. The operation's progress counters (ProcessedRows, SuccessfulRows, FailedRows) update as rows complete.

**Completed**
All rows processed successfully with zero failures.

**CompletedWithErrors**
All rows were attempted but some failed validation or processing. The operation finished normally.

**Failed**
The operation itself failed due to an unhandled exception, metadata validation failure, or infrastructure error. Processing may have been partially completed.

## Storage Concepts

**File Storage**
Where uploaded raw files (CSV, JSON) are persisted. Implementations: FileSystem (local disk), InMemory (testing), S3 (Amazon), or custom `IFileStorageProvider`.

**Metadata Storage**
Where operation records, errors, file metadata, and step statuses are persisted. Implementations: InMemory (default), Entity Framework (SQL Server), or custom repositories.

**Managed Storage Provider**
Internal component that coordinates file storage and metadata storage, ensuring both the file and its metadata record are created together.

## Scheduling Concepts

**Scheduler**
The component that decides when and how operations are processed. Implementations: ChannelsScheduler (background workers), ImmediateScheduler (synchronous, for testing), or custom `IBulkScheduler`.

**Worker**
A background task within the ChannelsScheduler that pulls operations from a bounded channel and processes them. `WorkerCount` controls how many workers run concurrently.

## Builder Concepts

**Builder Pattern**
BulkSharp uses `BulkSharpBuilder` with three configuration axes: file storage, metadata storage, and scheduling. Each axis has a sub-builder with `Use*` methods.

**Assembly Scanning**
The process of discovering `[BulkOperation]`-decorated types at startup. By default, scans all loaded assemblies. Can be restricted via `AddOperationsFromAssembly()` or `AddOperationsFromAssemblyOf<T>()`.

## Error Handling

**Error Flush**
Errors are buffered in memory and written to the repository in batches. `FlushBatchSize` (default: 100) controls how many rows are processed between flushes.

**Row Version**
A concurrency token on `BulkOperation` used for optimistic concurrency in EF Core. Prevents lost updates when multiple threads flush progress simultaneously.

**Signal Key**
A unique string identifier used to complete an async step externally. Generated during step execution and stored with the row's step status. External systems use this key to signal completion via REST API or `IBulkStepSignalService`.
