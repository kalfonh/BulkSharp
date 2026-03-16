<p align="center">
  <img src="docs/images/banner.svg" alt="BulkSharp" width="100%" />
</p>

<p align="center">
  <a href="https://github.com/kalfonh/BulkSharp/actions/workflows/ci.yml"><img src="https://github.com/kalfonh/BulkSharp/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="MIT License" /></a>
  <a href="https://github.com/kalfonh/BulkSharp"><img src="https://img.shields.io/badge/.NET-512BD4?logo=dotnet" alt=".NET" /></a>
</p>

---

## Quick Start

```bash
dotnet add package BulkSharp
```

Define an operation and register it:

```csharp
[BulkOperation("import-users")]
public class UserImport : IBulkRowOperation<UserMetadata, UserRow>
{
    public Task ValidateMetadataAsync(UserMetadata meta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(meta.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }
    public Task ValidateRowAsync(UserRow row, UserMetadata meta, CancellationToken ct)
    {
        if (!row.Email.Contains('@'))
            throw new BulkValidationException("Invalid email.");
        return Task.CompletedTask;
    }
    public Task ProcessRowAsync(UserRow row, UserMetadata meta, CancellationToken ct)
    {
        // Your business logic here
        return Task.CompletedTask;
    }
}

// Register
services.AddBulkSharp();

// Run
var service = provider.GetRequiredService<IBulkOperationService>();
var id = await service.CreateBulkOperationAsync("import-users", csvStream, "users.csv", metadata, "admin");
```

> See the full [Getting Started](https://github.com/kalfonh/BulkSharp/blob/main/docs/getting-started/quick-start.md) guide for a complete walkthrough with models, CSV mapping, and error querying.

## How It Works

```
Upload File ──> Validate ──> Store ──> Schedule ──> Stream Rows ──> Process ──> Track
                                                        │
                                          ┌─────────────┼─────────────┐
                                          ▼             ▼             ▼
                                     Validate Row   Process Row   Record Error
                                                    (or Steps)    (per row)
```

1. **Create** -- Upload a CSV/JSON file with metadata. BulkSharp stores the file and creates an operation record.
2. **Schedule** -- The operation is queued for background processing via the configured scheduler.
3. **Process** -- Rows are streamed via `IAsyncEnumerable<T>`. Each row is validated, then processed. Failures are recorded per-row without stopping the operation.
4. **Track** -- Query operation status, progress, and paginated error details at any time.

States: `Pending` -> `Validating` -> `Running` -> `Completed` | `CompletedWithErrors` | `Failed` | `Cancelled`

## Configuration

```csharp
services.AddBulkSharp(builder => builder
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 4)
    .UseFileStorage(fs => fs.UseFileSystem("data/uploads"))
    .UseMetadataStorage(ms => ms.UseSqlServer(opts => opts.ConnectionString = connStr))
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 4)));
```

| Axis | Options |
|------|---------|
| **File Storage** | `UseFileSystem()`, `UseInMemory()`, `UseS3()`, `UseCustom<T>()` |
| **Metadata** | `UseInMemory()`, `UseSqlServer()`, `UseEntityFramework<T>()`, `UseCustom()` |
| **Scheduler** | `UseChannels()`, `UseImmediate()`, `UseCustom<T>()` |

## Step-Based Operations

For multi-phase workflows, each step gets its own retry policy with exponential backoff:

```csharp
[BulkOperation("onboard-employees")]
public class EmployeeOnboarding : IBulkPipelineOperation<Meta, Row>
{
    // ... validation methods ...
    public IEnumerable<IBulkStep<Meta, Row>> GetSteps()
    {
        yield return new CreateAdAccountStep();   // MaxRetries = 3
        yield return new AssignEquipmentStep();    // MaxRetries = 2
        yield return new SendWelcomeEmailStep();   // MaxRetries = 1
    }
}
```

Async steps support **polling** (periodic completion checks) and **signal** (external REST callback) modes for long-running external work.

## Dashboard

Drop-in Blazor Server UI for monitoring and managing operations:

```csharp
services.AddBulkSharpDashboard();
app.UseBulkSharpDashboard();
```

Operation list with filtering, progress tracking, per-row error drill-down, file upload with real-time validation, and a full REST API.

## Packages

| Package | Description |
|---------|-------------|
| `BulkSharp` | Meta-package with DI registration, builders, and defaults |
| `BulkSharp.Core` | Abstractions, domain models, and attributes |
| `BulkSharp.Processing` | Processing engine, data formats, storage, and scheduling |
| `BulkSharp.Dashboard` | Blazor Server monitoring UI |
| `BulkSharp.Data.EntityFramework` | SQL Server persistence via EF Core |
| `BulkSharp.Files.S3` | Amazon S3 file storage |
| `BulkSharp.Gateway` | Multi-service API gateway with routing and aggregation |

Most consumers only need `BulkSharp`. Add optional packages as needed.

## Samples

```bash
dotnet run --project samples/BulkSharp.Sample.UserImport      # Console app
dotnet run --project samples/BulkSharp.Sample.Dashboard        # Web app + dashboard
dotnet run --project samples/BulkSharp.Sample.Production.AppHost  # Aspire + EF + S3
```

## Documentation

- [Getting Started](https://github.com/kalfonh/BulkSharp/blob/main/docs/getting-started/quick-start.md)
- [Architecture](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/architecture.md)
- [Configuration](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/configuration.md)
- [Step Operations](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/step-operations.md)
- [Async Steps](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/async-steps.md)
- [Dashboard](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/dashboard.md)
- [Error Handling](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/error-handling.md)
- [Custom Providers](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/custom-providers.md)
- [Production Deployment](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/production.md)

## License

[MIT](LICENSE)
