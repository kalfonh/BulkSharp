# Database Setup

BulkSharp stores operation metadata, row records, and file metadata in SQL Server via the optional `BulkSharp.Data.EntityFramework` package. This guide covers schema creation and management.

## Schema Creation

BulkSharp does not ship EF Core migrations. Choose one of the following approaches based on your deployment model.

### Option 1: Consumer-Owned DbContext (Recommended)

If your application has its own `DbContext`, inherit from `BulkSharpDbContext` and manage migrations in your project:

```csharp
public class AppDbContext : BulkSharpDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Your additional DbSets here
    public DbSet<MyEntity> MyEntities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // applies BulkSharp entity configuration
        // Your additional configuration here
    }
}

// Registration
services.AddBulkSharp(builder => builder
    .UseMetadataStorage(ms => ms.UseEntityFramework<AppDbContext>()));
```

Then use standard EF Core migration commands:

```bash
dotnet ef migrations add AddBulkSharpSchema -c AppDbContext
dotnet ef database update -c AppDbContext
```

### Option 2: Built-in BulkSharpDbContext

If you don't need a custom DbContext, use `UseSqlServer` directly:

```csharp
services.AddBulkSharp(builder => builder
    .UseMetadataStorage(ms => ms.UseSqlServer(opts =>
        opts.ConnectionString = connectionString)));
```

For initial schema creation, call `EnsureCreated` at startup:

```csharp
using var scope = app.Services.CreateScope();
var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BulkSharpDbContext>>();
using var ctx = factory.CreateDbContext();
await ctx.Database.EnsureCreatedAsync();
```

**Note:** `EnsureCreated` does not support incremental schema changes. For production, use Option 1 with migrations or manage schema via SQL scripts.

## Schema Overview

BulkSharp creates three tables:

| Table | Purpose |
|---|---|
| `BulkOperations` | Operation records with status, counters, metadata JSON |
| `BulkFiles` | File metadata with soft-delete support |
| `BulkRowRecords` | Per-row processing records (validation, steps, errors) |

### Key Indexes

- `BulkOperations`: Status, CreatedAt, OperationName, CreatedBy
- `BulkRowRecords`: BulkOperationId, State, ErrorType (filtered), SignalKey (filtered), composite unique (BulkOperationId, RowNumber, StepIndex)
- `BulkFiles`: UploadedAt, IsDeleted, composite (StorageProvider, StorageKey)

### Concurrency Control

`BulkOperations` uses a `RowVersion` column for optimistic concurrency. The EF repository retries up to 5 times on `DbUpdateConcurrencyException`, merging monotonically-increasing counters via `Math.Max`.

## Retry Configuration

The SQL Server provider configures retry-on-failure by default:

```csharp
services.AddBulkSharp(builder => builder
    .UseMetadataStorage(ms => ms.UseSqlServer(opts =>
    {
        opts.ConnectionString = connectionString;
        opts.MaxRetryCount = 3;        // default
        opts.MaxRetryDelay = TimeSpan.FromSeconds(5); // default
    })));
```

This handles transient SQL Server errors (deadlocks, connection resets, timeouts) automatically.
