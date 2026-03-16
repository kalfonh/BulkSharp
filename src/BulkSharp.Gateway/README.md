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

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [Gateway Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/gateway.md)
