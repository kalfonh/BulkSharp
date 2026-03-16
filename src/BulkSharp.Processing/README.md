# BulkSharp.Processing

Processing engine for BulkSharp bulk data operations. Provides the runtime pipeline, data format parsers, storage implementations, and schedulers.

## What's in this package

- **Processors**: Operation processor, typed processor, step executor with retry and backoff
- **Data formats**: CSV parser (via CsvHelper), JSON parser, extensible format factory
- **Storage**: File system and in-memory implementations for file and metadata storage
- **Schedulers**: Channels-based background scheduler, immediate scheduler for testing
- **Services**: Row validation pipeline, flush service, event dispatcher, signal service

## When to reference directly

Most consumers should reference the `BulkSharp` meta-package instead. Reference `BulkSharp.Processing` directly only when building advanced integrations that need access to internal processing services.

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [Architecture Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/architecture.md)
