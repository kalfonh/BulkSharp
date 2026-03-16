# BulkSharp

Production-grade .NET 8 library for defining, executing, and tracking bulk data operations from CSV/JSON files.

## Quick Start

```csharp
// Register services (defaults: file system storage, in-memory metadata, Channels scheduler)
services.AddBulkSharp();

// Or configure explicitly
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseFileSystem("data/uploads"))
    .UseMetadataStorage(ms => ms.UseSqlServer(connectionString))
    .UseScheduler(s => s.UseChannels()));
```

## Define a Bulk Operation

```csharp
public class UserMetadata : IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
}

[CsvSchema("1.0")]
public class UserRow : IBulkRow
{
    [CsvColumn(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [CsvColumn(nameof(Email))]
    public string Email { get; set; } = string.Empty;
}

[BulkOperation("import-users")]
public class UserImportOperation : IBulkRowOperation<UserMetadata, UserRow>
{
    public Task ValidateMetadataAsync(UserMetadata metadata, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(UserRow row, UserMetadata metadata, CancellationToken ct = default)
    {
        if (!row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email: {row.Email}");
        return Task.CompletedTask;
    }

    public async Task ProcessRowAsync(UserRow row, UserMetadata metadata, CancellationToken ct = default)
    {
        // Your business logic here
    }
}
```

## Execute

```csharp
var service = provider.GetRequiredService<IBulkOperationService>();

var operationId = await service.CreateBulkOperationAsync(
    "import-users", csvStream, "users.csv",
    new UserMetadata { RequestedBy = "admin" }, "admin");

var operation = await service.GetBulkOperationAsync(operationId);
// operation.Status, operation.ProcessedRows, operation.TotalRows, operation.FailedRows
```

## Step-Based Operations

For multi-step workflows with per-step retry and exponential backoff:

```csharp
[BulkOperation("onboard-employees")]
public class EmployeeOnboarding : IBulkPipelineOperation<Metadata, EmployeeRow>
{
    public IEnumerable<IBulkStep<Metadata, EmployeeRow>> GetSteps()
    {
        yield return new CreateAccountStep();    // MaxRetries = 3
        yield return new SendWelcomeEmailStep(); // MaxRetries = 1
    }
    // ... ValidateMetadataAsync, ValidateRowAsync, ProcessRowAsync
}
```

## Features

- Typed operations with metadata and row validation
- Step-based operations with ordered steps and retry
- CSV and JSON streaming via `IAsyncEnumerable<T>`
- Pluggable file storage (file system, in-memory, custom)
- Pluggable metadata storage (in-memory, SQL Server/EF Core)
- Background scheduling (Channels-based, immediate, custom)
- Blazor monitoring dashboard (separate `BulkSharp.Dashboard` package)

## License

MIT
