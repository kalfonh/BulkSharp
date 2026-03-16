# BulkSharp.Core

Core abstractions, domain models, attributes, and configuration for the BulkSharp bulk data processing library.

## What's in this package

- **Interfaces**: `IBulkRowOperation`, `IBulkPipelineOperation`, `IBulkStep`, `IBulkMetadata`, `IBulkRow`
- **Storage abstractions**: `IFileStorageProvider`, `IBulkOperationRepository`, `IBulkRowRecordRepository`
- **Domain models**: `BulkOperation`, `BulkRowRecord`, `BulkFile`, status enums
- **Attributes**: `[BulkOperation]`, `[BulkStep]`, `[CsvColumn]`, `[CsvSchema]`
- **Builders**: `BulkSharpBuilder`, `FileStorageBuilder`, `MetadataStorageBuilder`, `SchedulerBuilder`
- **Configuration**: `BulkSharpOptions`

## When to reference directly

Most consumers should reference the `BulkSharp` meta-package instead. Reference `BulkSharp.Core` directly only when building custom storage providers, schedulers, or extension packages that need the abstractions without the processing engine.

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [Getting Started](https://github.com/kalfonh/BulkSharp#quick-start)
