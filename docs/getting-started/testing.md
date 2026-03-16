# Testing

BulkSharp provides in-memory implementations of all storage and scheduling components, making it straightforward to test operations without external dependencies.

## Setup

```csharp
using BulkSharp;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseInMemory())
    .UseMetadataStorage(ms => ms.UseInMemory())
    .UseScheduler(s => s.UseImmediate()));
services.AddLogging();

var provider = services.BuildServiceProvider();
```

Or use the convenience method:

```csharp
services.AddBulkSharpInMemory();
```

Key differences from production:
- `UseInMemory()` file storage keeps files in memory (no disk I/O)
- `UseInMemory()` metadata storage uses `ConcurrentDictionary` repositories
- `UseImmediate()` scheduler processes operations inline (no background threads)

## Unit testing an operation

Test your validation and processing logic directly:

```csharp
[Fact]
public async Task ValidateRowAsync_InvalidEmail_Throws()
{
    var operation = new UserImportOperation();
    var metadata = new UserMetadata { RequestedBy = "admin" };
    var row = new UserRow { FirstName = "Test", Email = "invalid" };

    await Assert.ThrowsAsync<BulkValidationException>(() =>
        operation.ValidateRowAsync(row, metadata));
}
```

## Integration testing the full pipeline

Test the complete flow from file upload through processing:

```csharp
[Fact]
public async Task FullPipeline_ValidCsv_CompletesSuccessfully()
{
    var services = new ServiceCollection();
    services.AddBulkSharpInMemory();
    services.AddLogging();
    var provider = services.BuildServiceProvider();

    var service = provider.GetRequiredService<IBulkOperationService>();
    var processor = provider.GetRequiredService<IBulkOperationProcessor>();

    var csv = "FirstName,LastName,Email\nAlice,Smith,alice@test.com\n";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var id = await service.CreateBulkOperationAsync(
        "import-users", stream, "test.csv",
        new UserMetadata { RequestedBy = "admin" }, "test");

    await processor.ProcessOperationAsync(id);

    var op = await service.GetBulkOperationAsync(id);
    Assert.Equal(BulkOperationStatus.Completed, op!.Status);
    Assert.Equal(1, op.SuccessfulRows);
    Assert.Equal(0, op.FailedRows);
}
```

## Testing pre-submission validation

```csharp
[Fact]
public async Task Validate_EmptyMetadata_ReturnsErrors()
{
    // ... setup ...
    var result = await service.ValidateBulkOperationAsync(
        "import-users", "{}", Stream.Null, "test.csv");

    Assert.False(result.IsValid);
    Assert.NotEmpty(result.MetadataErrors);
}
```

## Next Steps

- [Quick Start](quick-start.md) - Basic setup walkthrough
- [Error Handling](../guides/error-handling.md) - Testing error scenarios
