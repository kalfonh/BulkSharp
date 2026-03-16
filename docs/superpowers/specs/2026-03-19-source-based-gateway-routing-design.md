# Source-Based Gateway Routing

## Problem

The gateway fans out every list query (`GET /api/bulks`) to all backends, merges results, and re-paginates. When the caller already knows which backend owns the data, this is wasteful. The `Source` property on `BulkOperation` exists but is nullable and not leveraged for routing optimization.

## Goals

1. Make `BulkOperation.Source` a reliable, always-present identifier for the owning backend service.
2. Allow the gateway to short-circuit list queries to a single backend when the caller specifies a `source` parameter.
3. Keep backward compatibility for single-service deployments (no required configuration).

## Non-Goals

- Adding `Source` as a repository-level query filter. `Source` is a gateway routing concern, not a per-backend data filter.
- Changing the fan-out behavior for `RouteBySourceServiceAsync` (operation-id-based routing). That path already uses caching and works well.

## Design

### 1. Domain Model: `BulkOperation.Source` becomes required

**File:** `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs`

Change `Source` from `string?` to `string`. Default to `string.Empty` so existing persisted records don't break deserialization.

**File:** `src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs`

Update the EF Core mapping to add `.IsRequired()` on the `Source` property. A database migration is required to backfill existing NULL `Source` rows with a default value (e.g., `"unknown"`) before altering the column to NOT NULL. The migration should be documented but not generated as part of this change (consumers generate their own migrations).

### 2. Auto-default `BulkSharpOptions.ServiceName`

**File:** `src/BulkSharp.Core/Configuration/BulkSharpOptions.cs`, `src/BulkSharp/ServiceCollectionExtensions.cs`

`ServiceName` stays `string?` in the options class. In `AddBulkSharp()` (in `ServiceCollectionExtensions.cs`), **before** the `AddOptions<BulkSharpOptions>().PostConfigure(...)` call, apply the default: if `ServiceName` is null after the user's configure action, set it to `Assembly.GetEntryAssembly()?.GetName().Name ?? "default"`. This must happen before `PostConfigure` because the existing `PostConfigure` chain calls `options.Validate()` and ordering between multiple `PostConfigure` registrations is fragile. The same defaulting must also be applied in `AddBulkSharpApi()` which has its own options registration path.

Note: `Assembly.GetEntryAssembly()` returns `null` in xUnit test hosts, so tests that don't explicitly set `ServiceName` will get `"default"` as the value. This is acceptable and should be covered by a dedicated unit test.

### 3. Gateway Aggregator: Single-backend short-circuit

**File:** `src/BulkSharp.Gateway/Services/GatewayAggregator.cs`

Currently `GatewayAggregator` depends on `GatewayRouter` but has no direct access to `IBackendClientFactory` for resolving a backend by service name. Add a new method to `GatewayRouter`:

```csharp
public IBackendClient? GetClientByServiceName(string serviceName)
```

This checks the list from `IBackendClientFactory.GetAllClients()` for a client whose `ServiceName` matches. Returns that client if found, null otherwise. It must **not** call `GetClient(name)` directly because `BackendClientFactory.GetClient` always returns a `BackendClient` (never null) — passing an unregistered name creates an `HttpClient` with no configured base address, which would fail at runtime instead of returning null.

In `AggregateListAsync`, before fanning out:

1. Parse the incoming query string (using `HttpUtility.ParseQueryString`, already available in the codebase).
2. Check for a `source` parameter.
3. If present, call `router.GetClientByServiceName(source)` to resolve the backend.
4. If a match is found, reconstruct the query string without the `source` parameter. If `source` was the only parameter, pass an empty string (not `"?"`) since `GetBulksAsync` concatenates it directly onto the URL path. Otherwise preserve the leading `?` and all remaining parameters. Call `client.GetBulksAsync(strippedQueryString)` and return the response directly — no merge, no re-sort, no re-pagination.
5. If no match is found, fall through to the existing fan-out behavior.

When `source` is absent, behavior is identical to today.

### 4. Sample Gateway

**Directory:** `samples/BulkSharp.Sample.Gateway`

- Configure backends with explicit `ServiceName` values.
- Show `Source` on operation listings in the dashboard so users can see which backend owns each operation.
- Include sample queries demonstrating the `source` filter parameter.

### 5. Testing

**Aggregator unit tests** (`tests/BulkSharp.Gateway.Tests/GatewayAggregatorTests.cs`):
- `source` present and matches a backend: routes to single backend, returns its response directly.
- `source` present but no match: falls through to fan-out.
- `source` absent: fan-out as today (existing tests cover this).
- `source` parameter is stripped from the forwarded query string.
- `source` as the only query parameter: forwards empty string, not `"?"`.

**ServiceName auto-default tests** (`tests/BulkSharp.UnitTests/Configuration/`):
- When `ServiceName` is null, `Build()` defaults it to the entry assembly name (or `"default"` in test hosts).
- When `ServiceName` is explicitly set, it is preserved.

**Integration tests** (`tests/BulkSharp.Gateway.IntegrationTests/GatewayEndpointTests.cs`):
- `GET /api/bulks?source=svc-a` returns only that backend's operations without contacting other backends.

## Affected Files

| File | Change |
|------|--------|
| `src/BulkSharp.Core/Domain/Operations/BulkOperation.cs` | `Source`: `string?` to `string` |
| `src/BulkSharp.Core/Configuration/BulkSharpOptions.cs` | Auto-default logic for `ServiceName` |
| `src/BulkSharp/ServiceCollectionExtensions.cs` | Apply `ServiceName` default before options registration (both `AddBulkSharp` and `AddBulkSharpApi`) |
| `src/BulkSharp.Data.EntityFramework/BulkSharpDbContext.cs` | Add `.IsRequired()` to `Source` mapping |
| `src/BulkSharp.Gateway/Routing/GatewayRouter.cs` | Add `GetClientByServiceName()` method |
| `src/BulkSharp.Gateway/Services/GatewayAggregator.cs` | Single-backend short-circuit in `AggregateListAsync` |
| `samples/BulkSharp.Sample.Gateway/` | Demonstrate `source` routing |
| `tests/BulkSharp.Gateway.Tests/GatewayAggregatorTests.cs` | New unit tests |
| `tests/BulkSharp.Gateway.IntegrationTests/GatewayEndpointTests.cs` | New integration test |

## Deployment Constraint

The gateway's backend `Name` (configured via `AddBackend("svc-a", ...)`) must exactly match the corresponding backend's `BulkSharpOptions.ServiceName`. The `source` query parameter carries the value from `BulkOperation.Source`, which is stamped from `BulkSharpOptions.ServiceName` on the backend that created it. The gateway matches this against `client.ServiceName`, which comes from the backend `Name` in gateway config. If these don't align, the short-circuit silently falls through to fan-out (no error, just no optimization). This should be validated at startup with a log warning if a `source` value is seen that doesn't match any registered backend.

## Testing Approach

`GatewayRouter` is injected as a concrete type (existing pattern). Aggregator unit tests use a real `GatewayRouter` with a mock `IBackendClientFactory` — no interface extraction needed. `GetClientByServiceName` is tested in isolation via `GatewayRouterTests`.

## Tradeoffs

- **Backend rename/split**: If a backend's `ServiceName` changes, persisted `Source` values in the DB become stale. The gateway's 1-hour cache TTL handles transient routing, but historical operations will still reference the old name. This is acceptable — renaming a service is a migration event.
- **Assembly name as default**: Using the entry assembly name is convenient but fragile if the assembly is renamed. For multi-service deployments, explicit `ServiceName` configuration should be documented as the recommended approach.
