# Quick Start

This guide walks through creating a console application that processes a CSV file of users using BulkSharp.

## Prerequisites

- .NET 8.0 SDK or later

## 1. Create the project

```bash
dotnet new console -n MyBulkApp
cd MyBulkApp
dotnet add package BulkSharp
dotnet add package Microsoft.Extensions.Hosting
```

## 2. Define your models

Create a metadata class that describes the operation and a row class that maps to your CSV columns.

```csharp
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;

public class UserMetadata : IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
    public string Department { get; set; } = "General";
}

[CsvSchema("1.0")]
public class UserRow : IBulkRow
{
    [CsvColumn("FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [CsvColumn("LastName")]
    public string LastName { get; set; } = string.Empty;

    [CsvColumn("Email")]
    public string Email { get; set; } = string.Empty;
}
```

- `IBulkMetadata` is a marker interface for operation-level data (who requested it, configuration, etc.)
- `IBulkRow` is a marker interface for individual row data
- `[CsvSchema]` declares the CSV format version
- `[CsvColumn]` maps CSV headers to properties

## 3. Create the operation

The operation class defines how to validate and process each row.

```csharp
using BulkSharp.Core.Abstractions;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Exceptions;

[BulkOperation("import-users", Description = "Import users from CSV")]
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
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.FirstName} {row.LastName}.");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(UserRow row, UserMetadata metadata, CancellationToken ct = default)
    {
        Console.WriteLine($"Processing: {row.FirstName} {row.LastName} ({row.Email})");
        return Task.CompletedTask;
    }
}
```

- `[BulkOperation("import-users")]` registers this operation by name. BulkSharp discovers it automatically via assembly scanning.
- `ValidateMetadataAsync` runs once before processing starts. Throw `BulkValidationException` to reject the entire operation.
- `ValidateRowAsync` runs per row. Throw to record a row-level error (processing continues with the next row).
- `ProcessRowAsync` runs per row after validation passes. This is where your business logic goes.

## 4. Wire up the host

```csharp
using BulkSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddBulkSharp();
    })
    .Build();

var service = host.Services.GetRequiredService<IBulkOperationService>();
var processor = host.Services.GetRequiredService<IBulkOperationProcessor>();
```

`AddBulkSharp()` with no arguments uses defaults:
- File system storage (writes to `bulksharp-storage/` directory)
- In-memory metadata repositories
- Channels-based background scheduler

## 5. Create a CSV file

Create a file called `users.csv`:

```csv
FirstName,LastName,Email
Alice,Johnson,alice@example.com
Bob,Smith,bob@example.com
Charlie,Brown,invalid-email
```

## 6. Run the operation

```csharp
await using var stream = File.OpenRead("users.csv");

var operationId = await service.CreateBulkOperationAsync(
    "import-users",
    stream,
    "users.csv",
    new UserMetadata { RequestedBy = "admin", Department = "Engineering" },
    "admin");

// Process immediately (the Channels scheduler does this in the background,
// but for a console app we can also process directly)
await processor.ProcessOperationAsync(operationId);

// Check results
var operation = await service.GetBulkOperationAsync(operationId);
Console.WriteLine($"Status: {operation!.Status}");
Console.WriteLine($"Total: {operation.TotalRows}, Success: {operation.SuccessfulRows}, Failed: {operation.FailedRows}");
```

Expected output:

```
Processing: Alice Johnson (alice@example.com)
Processing: Bob Smith (bob@example.com)
Status: CompletedWithErrors
Total: 3, Success: 2, Failed: 1
```

Row 3 ("Charlie Brown") fails validation because "invalid-email" has no `@`. BulkSharp records the error and continues processing the remaining rows.

## 7. Query errors

```csharp
var rowRecordRepo = serviceProvider.GetRequiredService<IBulkRowRecordRepository>();
var errors = await rowRecordRepo.QueryAsync(
    new BulkRowRecordQuery { OperationId = operationId, ErrorsOnly = true });

foreach (var record in errors.Items)
{
    Console.WriteLine($"Row {record.RowNumber}: {record.ErrorMessage}");
}
```

Output:

```
Row 3: Invalid email for Charlie Brown.
```

## Next Steps

- [ASP.NET Core Integration](aspnet-integration.md) - Add BulkSharp to a web app with the dashboard
- [Testing](testing.md) - Use in-memory providers for unit and integration tests
- [Step-Based Operations](../guides/step-operations.md) - Break processing into multiple steps with retry
- [Configuration](../guides/configuration.md) - Full options reference
