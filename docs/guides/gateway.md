# Gateway

For microservice architectures where multiple backend services each run BulkSharp with domain-specific operations, `BulkSharp.Gateway` provides a unified API surface that the Dashboard UI talks to.

## Architecture

```
Dashboard UI → BulkSharp.Gateway → Backend Service A (devices)
                                 → Backend Service B (orders)
                                 → Backend Service C (inventory)
```

Each backend registers `AddBulkSharp()` with its own storage and scheduler. The gateway aggregates discovery, routes requests, and proxies responses.

## Setup

### Install

```bash
dotnet add package BulkSharp.Gateway
```

### Gateway Host

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("device-service", "https://device-svc.internal")
    .AddBackend("order-service", "https://order-svc.internal"));

builder.Services.AddBulkSharpDashboard();

var app = builder.Build();
app.UseBulkSharpGateway();
app.UseBulkSharpDashboard();
app.Run();
```

### Backend Services

Each backend's `ServiceName` must match its name in the gateway's `AddBackend()` call. This enables both ID-based routing and source-based list filtering:

```csharp
builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts => opts.ServiceName = "device-service")
    .UseFileStorage(fs => fs.UseS3(...))
    .UseMetadataStorage(ms => ms.UseSqlServer(...))
    .UseScheduler(s => s.UseChannels()));
```

### With Aspire

```csharp
// AppHost
var deviceService = builder.AddProject<Projects.DeviceService>("device-service");
var orderService = builder.AddProject<Projects.OrderService>("order-service");

var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(deviceService)
    .WithReference(orderService);

// Gateway Program.cs — names resolve via service discovery
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("device-service")
    .AddBackend("order-service"));
```

## How It Works

### Operation Discovery

On startup and periodically (`RegistryRefreshInterval`, default 5 min), the gateway calls `GET /api/operations` on each backend and builds an operation-name → service mapping. Duplicate names across backends are rejected at startup.

### Request Routing

| Request Type | Routing Strategy |
|---|---|
| Create / Validate / Template | By operation name (from discovery cache) |
| Detail / Errors / Rows / Status / File / Cancel / Signal | By `Source` property on `BulkOperation` (cached in MemoryCache) |
| List (with `source`) | Direct to named backend (no fan-out) |
| List (without `source`) / Discovery | Fan-out to all backends, merge results |

### ID-Based Routing

Each `BulkOperation` has a `Source` property set by the backend during creation. The gateway reads this from API responses and caches `operationId → serviceName` in `MemoryCache` with 1-hour sliding expiration. On cache miss, the gateway fans out to all backends — the first 200 response wins.

### Aggregated List

`GET /api/bulks` merges results across backends, sorting by `createdAt` descending. Failed backends contribute zero results.

To page correctly across backends the gateway cannot simply forward the caller's page — asking each backend for page 2 would only ever surface `pageSize x backendCount` rows, which is not the true second page of the merged ordering. Instead it requests a prefix from each backend deep enough to cover the requested page, then slices the merge.

That prefix is bounded at **1000 rows per backend**. Beyond it, results may be incomplete and a warning is logged rather than silently truncating — a short page otherwise reads to a client as "there is no more data". Clients paging deeper should pass `source` to page a single backend, which pages natively with no bound.

#### Source-Based Routing

When the caller includes a `source` query parameter, the gateway skips fan-out and routes directly to the named backend:

```
GET /api/bulks?source=device-service&page=1&pageSize=20
```

The `source` value must match a backend name registered via `AddBackend()`. It is stripped before forwarding — the backend never sees it. If the value doesn't match any registered backend, the gateway logs a warning and falls back to fan-out.

Backend names are the gateway's own identifiers and do **not** need to match a backend's `BulkSharpOptions.ServiceName`. Routing follows the backend that answers, not the value it reports.

## Configuration

```csharp
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("device-service", "https://device-svc.internal")
    .ConfigureResilience(opts =>
    {
        opts.HttpTimeout = TimeSpan.FromSeconds(15);
        opts.HttpRetryCount = 3;
        opts.CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30);
        opts.RegistryRefreshInterval = TimeSpan.FromMinutes(2);
        opts.FanOutTimeoutPerBackend = TimeSpan.FromSeconds(5);
    }));
```

## Resilience

- Each backend gets its own named `HttpClient` with retry + circuit breaker
- One backend down never takes down the gateway
- Fan-out endpoints skip failed backends and return partial results
- `CancellationToken` from the HTTP request context is forwarded to all backend calls

## Authorization

Reads and writes are governed separately, so viewers can be prevented from mutating:

```csharp
app.UseBulkSharpGateway(new BulkSharpAuthorizationOptions
{
    ReadPolicy    = "bulk:read",
    OperatePolicy = "bulk:operate"
});
```

`OperatePolicy` falls back to `ReadPolicy` when omitted. A single policy for everything:

```csharp
app.UseBulkSharpGateway(authorizationPolicy: "BulkSharpAdmin");
```

Passing nothing leaves every endpoint unauthorized, including reads — appropriate only when your own middleware enforces access.

### Backend credentials

By default the caller's bearer token is forwarded to each backend, so backends authorize the original caller rather than an anonymous gateway. Supply a different credential model with a delegating handler:

```csharp
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("device-service", "https://device-svc.internal")
    .ConfigureResilience(opts => opts.ForwardBearerToken = false)
    .AddBackendHandler<MyServiceCredentialHandler>());
```

Handlers run ahead of the resilience pipeline, so retried requests carry the credential.

## Health

`AddBulkSharpGateway` registers a `bulksharp-backends` health check tagged `ready`. It reports **degraded** when only some backends are reachable — the gateway still correctly serves the operations owned by the rest — and **unhealthy** when none are, or when no backends are configured.

```csharp
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
```

Keeping liveness separate matters: an unreachable backend should not cause the orchestrator to restart a gateway that is working as designed.

## API contract

The gateway exposes the same routes, verbs and response shapes as `BulkSharp.Api`, enforced by `ContractConformanceTests` rather than by convention. A client generated from the OpenAPI document works against either. Two additions are gateway-specific and additive: `sourceService` on operation descriptors, and the `?source=` query parameter above.
