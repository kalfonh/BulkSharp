# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-08-09

Makes BulkSharp frontend-agnostic: an HTTP API product with an optional Blazor UI, rather
than a Blazor product with an API attached.

### Added
- `BulkSharp.Api` package containing the HTTP endpoints with no UI dependency. A host that
  references it without `BulkSharp.Dashboard` publishes no Razor or Blazor assemblies
- OpenAPI document at `/openapi/v1.json` with stable operation IDs, for generating clients
  in any technology stack
- Typed response contracts in `BulkSharp.Core.Contracts` for discovery, status, row errors,
  row progress, create, validate and signal responses
- `BulkSharpRoutes`, the single declaration of the HTTP route table, mapped by both the API
  and the gateway and enforced by a conformance test
- `AddBulkSharpCors()` for browser clients, exposing `Content-Disposition` so downloads
  retain their filenames
- Separate read and operate authorization policies via `BulkSharpAuthorizationOptions`
- `IBulkUserResolver` for deriving operation attribution from the authenticated principal
- Operation event feed at `/api/events` and `/api/bulks/{id}/events`, so a user interface in
  any process can render notifications
- `IBulkOperationEventStore` with in-memory and Entity Framework implementations; the EF
  store sequences events from a database identity column so horizontally scaled instances
  share one ordering
- `AddBackendHandler<T>()` on the gateway builder for supplying backend credentials
- Backend reachability health check, tagged `ready`
- `UseBulkSharpDashboardUi()` for mounting the UI when the API is served elsewhere
- Guide: Building a custom dashboard

### Changed
- **Breaking.** Enums serialize as strings rather than integers. .NET clients deserializing
  with default options must pass `BulkSharpJsonSerialization.Options`; clients in other
  languages are unaffected
- **Breaking.** Route constraints are uniform (`{id:guid}`), so a malformed identifier
  returns 404 rather than a model-binding failure
- `ToastService` is scoped rather than singleton, so notifications are per browser session
  instead of shared between all connected users
- `/api/operations` reports metadata requiredness that matches what the operation actually
  enforces

### Fixed
- Operation attribution came from the request body, allowing any caller to attribute an
  operation to another user
- Read endpoints were unauthenticated; only mutating endpoints honoured a policy
- The gateway called backends with no credential, so backends could not authorize the
  original caller
- The gateway routed by a backend's self-reported service name. Where that differed from
  the configured backend name, every request for an operation the gateway had not itself
  created failed — which included all pre-existing operations after a restart
- Fan-out paging returned incorrect pages across two or more backends
- `FanOutTimeoutPerBackend` was configurable but never applied
- Removed `GET /api/bulks/{id}/row-items`, exposed by the gateway but implemented by no
  backend

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
