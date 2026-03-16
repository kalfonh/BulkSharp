# BulkSharp.Data.EntityFramework

SQL Server persistence provider for BulkSharp using Entity Framework Core.

## Features

- Operation, row record, and file metadata persistence via EF Core
- Optimistic concurrency with row versioning
- Retry-on-failure for transient SQL errors
- `IDbContextFactory` pattern for thread-safe background processing
- Batched row record updates for high-throughput scenarios

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
