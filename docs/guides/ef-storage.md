# EF Core Storage

BulkSharp provides SQL Server persistence via Entity Framework Core through the `BulkSharp.Data.EntityFramework` package.

## Installation

```bash
dotnet add package BulkSharp.Data.EntityFramework
```

## Quick Setup

```csharp
services.AddBulkSharpSqlServer(opts =>
{
    opts.ConnectionString = connectionString;
    opts.MaxRetryCount = 5;                          // EF retry on transient failures
    opts.MaxRetryDelay = TimeSpan.FromSeconds(30);   // Max delay between retries
});

services.AddBulkSharp();
```

This registers `BulkSharpDbContext` with SQL Server and all EF-based repositories.

## Custom DbContext

If you need to extend the schema or share a DbContext with your application:

```csharp
public class AppDbContext : BulkSharpDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MyEntity> MyEntities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Your additional configuration
    }
}

// Registration
services.AddBulkSharpEntityFramework<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
services.AddBulkSharp();
```

## What Gets Persisted

EF storage provides repositories for:
- **Operations** -`BulkOperation` records with status, progress, timing
- **Files** - `BulkFile` metadata (the file content is stored by the file storage provider)
- **Row Records** - `BulkRowRecord` unified per-row tracking (validation, steps, errors, async completion)

## Thread Safety

EF repositories use `IDbContextFactory` internally, creating short-lived `DbContext` instances per operation. This makes them safe for parallel row processing (`MaxRowConcurrency > 1`).

## Optimistic Concurrency

`BulkOperation` includes a `RowVersion` property for optimistic concurrency. Concurrent updates to the same operation (e.g., progress updates from parallel workers) are handled via SQL Server's `rowversion` column.
