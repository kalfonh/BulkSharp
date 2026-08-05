# API Reference

Generated reference for every public type in BulkSharp. Use the navigation on the left to
browse by namespace.

## Where to start

| Package | Start here |
|---|---|
| `BulkSharp` | `ServiceCollectionExtensions` — `AddBulkSharp` and the builders |
| `BulkSharp.Core` | `BulkSharp.Core.Contracts` — the HTTP contract: routes, DTOs, JSON policy |
| `BulkSharp.Api` | `WebApplicationExtensions` — `MapBulkSharpEndpoints` and `MapBulkSharpOpenApi` |
| `BulkSharp.Dashboard` | `WebApplicationExtensions` — `UseBulkSharpDashboard` |
| `BulkSharp.Gateway` | `BulkSharpGatewayWebApplicationExtensions` — `UseBulkSharpGateway` |
| `BulkSharp.Data.EntityFramework` | `BulkSharpEntityFrameworkExtensions` |
| `BulkSharp.Files.S3` | `S3StorageBuilderExtensions` |

Extension points are marked with `[BulkExtensionPoint]`.

For task-oriented documentation see the [architecture guide](../guides/architecture.md), and
for building a front end outside .NET see
[Building a custom dashboard](../guides/building-a-custom-dashboard.md).
