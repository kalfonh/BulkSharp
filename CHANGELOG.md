# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-03-18

First public release.

### Added
- Core bulk operation framework with `IBulkRowOperation` and `IBulkPipelineOperation` interfaces
- CSV and JSON data format processors with stream-based `IAsyncEnumerable<T>` parsing
- Step-based operations with per-step retry, exponential backoff, and async completion (polling + signal modes)
- Pluggable file storage (FileSystem, InMemory, Amazon S3 via `BulkSharp.Files.S3`)
- Pluggable metadata persistence (InMemory, SQL Server via `BulkSharp.Data.EntityFramework`)
- Channels-based background scheduler with configurable worker count and backpressure
- Blazor Server dashboard with operation monitoring, progress tracking, error drill-down, and REST API
- Multi-service gateway for routing and aggregating across BulkSharp backends
- Per-row validation pipeline with composable validators
- Per-row error tracking with step-level granularity
- Event system with lifecycle hooks (Created, StatusChanged, Completed, Failed, RowFailed)
- Builder API with `AddBulkSharp()`, `AddBulkSharpInMemory()`, `AddBulkSharpApi()` convenience methods
- Orphaned step recovery service for signal-based async operations
- Comprehensive test suite (220 tests: unit, integration, architecture, dashboard, gateway)
- DocFX documentation site with 22 guide pages and full API reference

## [0.0.1] - 2026-03-16

Initial internal release.
