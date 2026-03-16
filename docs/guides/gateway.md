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

`GET /api/bulks` forwards the caller's query parameters to each backend, merges the small result sets, re-sorts by `CreatedAt` descending, and re-paginates. Failed backends contribute zero results.

#### Source-Based Routing

When the caller includes a `source` query parameter, the gateway skips fan-out and routes directly to the named backend:

```
GET /api/bulks?source=device-service&page=1&pageSize=20
```

The `source` value must match the backend name registered via `AddBackend()` (which must also match the backend's `BulkSharpOptions.ServiceName`). The `source` parameter is stripped before forwarding — the backend never sees it. If the value doesn't match any registered backend, the gateway logs a warning and falls back to fan-out.

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

```csharp
app.UseBulkSharpGateway(authorizationPolicy: "BulkSharpAdmin");
```

Mutating endpoints (create, cancel, signal) require the specified policy. Read endpoints are open.
