# BulkSharp.Gateway

API gateway for routing and aggregating across multiple BulkSharp backend services.

## Features

- Dynamic operation routing based on service registry
- Fan-out queries across all backends with aggregation
- HTTP resilience (retry, circuit breaker, timeout) via Microsoft.Extensions.Http.Resilience
- Periodic registry refresh from backend discovery endpoints
- Dashboard integration for unified monitoring

## Usage

```csharp
services.AddBulkSharpGateway(gateway =>
{
    gateway.AddBackend("service-a", "https://service-a:5000");
    gateway.AddBackend("service-b", "https://service-b:5000");
});

app.UseBulkSharpGateway();
```

## API contract

The gateway exposes the **same routes, verbs and response shapes** as `BulkSharp.Api`. A
client cannot tell whether it is talking to a backend or the gateway, so a single generated
client works against both, and the OpenAPI document published by `BulkSharp.Api` describes
the gateway too.

That equivalence is enforced by a test rather than by convention — see
`ContractConformanceTests` in `BulkSharp.Gateway.IntegrationTests`, which compares the
gateway's mapped routes against the API's and fails on any route present on one side only.

Two additions are gateway-specific, both additive:

| Addition | Endpoint | Behaviour |
|---|---|---|
| `sourceService` | `GET /api/operations` | Each descriptor is tagged with the backend that owns the operation. Null when served by a backend directly. |
| `?source={name}` | `GET /api/bulks` | Routes directly to one backend and skips the fan-out. The name must match a configured backend. |

### Backend naming

Backend names passed to `AddBackend(name, url)` are the gateway's own identifiers. They do
**not** need to match a backend's `BulkSharpOptions.ServiceName` — routing follows the
backend that answers, not the value it reports.

### Pagination

Fan-out queries merge results across backends by `createdAt`. To page correctly the gateway
over-fetches from each backend, bounded at 1000 rows per backend. Clients paging deeper than
that should pass `?source={name}` to route to a single backend, which pages natively without
a bound. A warning is logged whenever the bound truncates a result set.

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [Gateway Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/gateway.md)
