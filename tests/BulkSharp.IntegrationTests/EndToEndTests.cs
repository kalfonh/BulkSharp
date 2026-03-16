using System.Text.Json;
using BulkSharp;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class EndToEndTests
{
    [Fact]
    public async Task CreateAndProcessBulkOperation_WithValidData_ShouldCompleteSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name,Email,Age\nJohn Doe,john@test.com,30\nJane Smith,jane@test.com,25";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        // Assert
        var operation = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(operation);
        Assert.Equal(BulkOperationStatus.Completed, operation.Status);
        Assert.Equal(2, operation.TotalRows);
        Assert.Equal(2, operation.ProcessedRows);
        Assert.Equal(2, operation.SuccessfulRows);
        Assert.Equal(0, operation.FailedRows);
    }

    [Fact]
    public async Task CreateAndProcessBulkOperation_WithInvalidData_ShouldTrackErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var rowRecordRepo = provider.GetRequiredService<IBulkRowRecordRepository>();

        var csvContent = "Name,Email,Age\nJohn Doe,invalid-email,30\nJane Smith,jane@test.com,25";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        // Assert
        var operation = await operationService.GetBulkOperationAsync(operationId);
        var errors = await rowRecordRepo.QueryAsync(new BulkSharp.Core.Domain.Queries.BulkRowRecordQuery
        {
            OperationId = operationId,
            ErrorsOnly = true
        });

        Assert.NotNull(operation);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation.Status);
        Assert.Equal(2, operation.TotalRows);
        Assert.Equal(2, operation.ProcessedRows);
        Assert.Equal(1, operation.SuccessfulRows);
        Assert.Equal(1, operation.FailedRows);
        Assert.Single(errors.Items);
        Assert.NotNull(errors.Items.First().ErrorMessage);
    }
    
    [Fact]
    public async Task CreateAndProcessBulkOperation_WithInvalidMetadata_ShouldFail()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name,Email,Age\nJohn Doe,john@test.com,30";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "", Department = "IT" }; // Invalid metadata
        
        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);
        
        // Assert
        var operation = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(operation);
        Assert.Equal(BulkOperationStatus.Failed, operation.Status);
        Assert.NotNull(operation.ErrorMessage);
    }

    [Fact]
    public async Task CreateAndProcessBulkOperation_WithJsonFile_ShouldCompleteSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestJsonBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var rows = new[]
        {
            new TestJsonRow { Name = "Alice", Email = "alice@test.com", Age = 28 },
            new TestJsonRow { Name = "Bob", Email = "bob@test.com", Age = 35 }
        };
        var jsonContent = JsonSerializer.Serialize(rows);
        var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-json-operation", jsonStream, "users.json", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        // Assert
        var operation = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(operation);
        Assert.Equal(BulkOperationStatus.Completed, operation.Status);
        Assert.Equal(2, operation.TotalRows);
        Assert.Equal(2, operation.ProcessedRows);
        Assert.Equal(2, operation.SuccessfulRows);
        Assert.Equal(0, operation.FailedRows);
    }
}

[BulkOperation("test-operation")]
[Trait("Category", "Integration")]
public class TestBulkOperation : IBulkRowOperation<TestMetadata, TestCsvRow>
{
    public Task ValidateMetadataAsync(TestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }
    
    public Task ValidateRowAsync(TestCsvRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains("@"))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }
    
    public Task ProcessRowAsync(TestCsvRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate processing
        return Task.CompletedTask;
    }
}

[Trait("Category", "Integration")]
public class TestMetadata : BulkSharp.Core.Abstractions.Processing.IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
}

[CsvSchema("1.0")]
[Trait("Category", "Integration")]
public class TestCsvRow : BulkSharp.Core.Abstractions.Processing.IBulkRow
{
    [CsvColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [CsvColumn("Email")]
    public string Email { get; set; } = string.Empty;

    [CsvColumn("Age")]
    public int Age { get; set; }

    public string? RowId { get; set; }
}

[BulkOperation("test-json-operation")]
[Trait("Category", "Integration")]
public class TestJsonBulkOperation : IBulkRowOperation<TestMetadata, TestJsonRow>
{
    public Task ValidateMetadataAsync(TestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(TestJsonRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(TestJsonRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

[Trait("Category", "Integration")]
public class TestJsonRow : BulkSharp.Core.Abstractions.Processing.IBulkRow
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
    public string? RowId { get; set; }
}

