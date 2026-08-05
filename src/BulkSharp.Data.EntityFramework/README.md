# BulkSharp.Data.EntityFramework

SQL Server persistence provider for BulkSharp using Entity Framework Core.

## Features

- Operation, row record, and file metadata persistence via EF Core
- Durable operation event store, shared across service instances
- Optimistic concurrency with row versioning
- Retry-on-failure for transient SQL errors
- `IDbContextFactory` pattern for thread-safe background processing
- Batched row record updates for high-throughput scenarios

## Operation events

Adding this package replaces the default in-memory event store. That matters beyond
durability: the in-memory store keeps a per-process sequence counter, so a service running
more than one instance hands clients sequence numbers that are not comparable between them —
a UI polling with `?since=` either stalls or skips events depending on which instance
answers. Here the sequence comes from a database identity column, so all instances share one
ordering.

Events accumulate, one per lifecycle transition plus one per failed row. Prune on a schedule;
no retention window is imposed because the right one depends on how the feed is consumed:

```csharp
var store = (EntityFrameworkBulkOperationEventStore)provider
    .GetRequiredService<IBulkOperationEventStore>();

await store.PruneAsync(DateTime.UtcNow.AddDays(-7), cancellationToken);
```

## Usage

```csharp
services.AddBulkSharp(builder => builder
    .UseMetadataStorage(ms => ms.UseSqlServer(opts =>
        opts.ConnectionString = connectionString)));
```

Or with a custom DbContext:

```csharp
services.AddBulkSharp(builder => builder
    .UseMetadataStorage(ms => ms.UseEntityFramework<AppDbContext>()));
```

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [EF Storage Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/ef-storage.md)
- [Database Setup](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/database-setup.md)
