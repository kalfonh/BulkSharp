# ASP.NET Core Integration

This guide shows how to add BulkSharp with the Blazor dashboard to an ASP.NET Core application.

## 1. Install packages

```bash
dotnet add package BulkSharp
dotnet add package BulkSharp.Dashboard
```

For SQL Server persistence:

```bash
dotnet add package BulkSharp.Data.EntityFramework
```

## 2. Register services

In `Program.cs`:

```csharp
using BulkSharp;
using BulkSharp.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Option A: In-memory (development/testing)
builder.Services.AddBulkSharp();

// Option B: SQL Server persistence (production)
builder.Services.AddBulkSharpSqlServer(opts =>
    opts.ConnectionString = builder.Configuration.GetConnectionString("BulkSharp")!);
builder.Services.AddBulkSharp(b => b
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 4)
    .UseFileStorage(fs => fs.UseFileSystem("data/uploads"))
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 2)));

// Add the dashboard
builder.Services.AddBulkSharpDashboard();
```

## 3. Configure the pipeline

```csharp
var app = builder.Build();

app.UseBulkSharpDashboard();

app.Run();
```

The dashboard is now available at your application's root URL. It provides:

- Operation list with filtering and pagination
- Create new operations with file upload and real-time validation
- Operation details with progress, errors, and per-row step status
- File download for uploaded files
- REST API for programmatic access

## 4. REST API

The dashboard exposes a REST API alongside the Blazor UI:

```bash
# List registered operation types
curl http://localhost:5000/api/operations

# Create an operation
curl -X POST http://localhost:5000/api/bulks \
  -F "operationName=import-users" \
  -F "metadata={\"RequestedBy\":\"admin\"}" \
  -F "file=@users.csv"

# Check status
curl http://localhost:5000/api/bulks/{id}/status

# Query errors
curl "http://localhost:5000/api/bulks/{id}/errors?page=1&pageSize=50"

# Pre-submission validation
curl -X POST http://localhost:5000/api/bulks/validate \
  -F "operationName=import-users" \
  -F "metadata={\"RequestedBy\":\"admin\"}" \
  -F "file=@users.csv"
```

## 5. SQL Server setup

When using `AddBulkSharpSqlServer`, BulkSharp manages its own `BulkSharpDbContext`. You can use EF Core migrations or let BulkSharp create the schema at startup.

For a custom DbContext that extends `BulkSharpDbContext`:

```csharp
public class AppDbContext : BulkSharpDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Your additional DbSets here
}

// Registration
builder.Services.AddBulkSharpEntityFramework<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddBulkSharp();
```

## Next Steps

- [Testing](testing.md) - In-memory setup for tests
- [Dashboard Guide](../guides/dashboard.md) - Full dashboard features and configuration
- [S3 Storage](../guides/s3-storage.md) - Use Amazon S3 for file storage
