using BulkSharp;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class CsvProcessingTests
{
    [Fact]
    public async Task ProcessCsv_WithHeaders_ShouldParseCorrectly()
    {
        // Arrange
    var services = new ServiceCollection();
    services.AddBulkSharp(builder => { builder.UseFileStorage(fs => fs.UseInMemory()).UseMetadataStorage(ms => ms.UseInMemory()); builder.UseScheduler(s => s.UseImmediate()); });
        services.AddScoped<CsvTestOperation>();
        services.AddLogging();
        
        var provider = services.BuildServiceProvider();
    var jobService = provider.GetRequiredService<IBulkOperationService>();
    var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        
        var csvContent = "ProductName,Price,Category\nLaptop,999.99,Electronics\nBook,19.99,Education";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new CsvTestMetadata { ImportType = "Products" };
        
        // Act
    var jobId = await jobService.CreateBulkOperationAsync("csv-test", csvStream, "products.csv", metadata, "admin");
    await processor.ProcessOperationAsync(jobId);
        
        // Assert
    var job = await jobService.GetBulkOperationAsync(jobId);
    Assert.NotNull(job);
    Assert.Equal(BulkOperationStatus.Completed, job.Status);
    Assert.Equal(2, job.TotalRows);
    Assert.Equal(2, job.SuccessfulRows);
    }
    
    [Fact]
    public async Task ProcessCsv_WithMissingColumns_ShouldFailWithError()
    {
        // Arrange
    var services = new ServiceCollection();
    services.AddBulkSharp(builder => { builder.UseFileStorage(fs => fs.UseInMemory()).UseMetadataStorage(ms => ms.UseInMemory()); builder.UseScheduler(s => s.UseImmediate()); });
        services.AddScoped<CsvTestOperation>();
        services.AddLogging();
        
        var provider = services.BuildServiceProvider();
    var jobService = provider.GetRequiredService<IBulkOperationService>();
    var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        
        var csvContent = "ProductName,Price\nLaptop,999.99\nBook,19.99"; // Missing Category column
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new CsvTestMetadata { ImportType = "Products" };

        // Act
    var jobId = await jobService.CreateBulkOperationAsync("csv-test", csvStream, "products.csv", metadata, "admin");
    await processor.ProcessOperationAsync(jobId);

        // Assert — missing required CSV columns causes the operation to fail
    var job = await jobService.GetBulkOperationAsync(jobId);
    Assert.NotNull(job);
    Assert.Equal(BulkOperationStatus.Failed, job.Status);
    Assert.NotNull(job.ErrorMessage);
    }
}

[BulkOperation("csv-test")]
[Trait("Category", "Integration")]
public class CsvTestOperation : IBulkRowOperation<CsvTestMetadata, ProductCsvRow>
{
    public Task ValidateMetadataAsync(CsvTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.ImportType))
            throw new BulkValidationException("ImportType is required.");
        return Task.CompletedTask;
    }
    
    public Task ValidateRowAsync(ProductCsvRow row, CsvTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.ProductName))
            throw new BulkValidationException("ProductName is required.");
        if (row.Price <= 0)
            throw new BulkValidationException("Price must be greater than 0.");
        return Task.CompletedTask;
    }
    
    public Task ProcessRowAsync(ProductCsvRow row, CsvTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate processing
        return Task.CompletedTask;
    }
}

[Trait("Category", "Integration")]
public class CsvTestMetadata : BulkSharp.Core.Abstractions.Processing.IBulkMetadata
{
    public string ImportType { get; set; } = string.Empty;
}

[CsvSchema("1.0")]
[Trait("Category", "Integration")]
public class ProductCsvRow : BulkSharp.Core.Abstractions.Processing.IBulkRow
{
    [CsvColumn("ProductName")]
    public string ProductName { get; set; } = string.Empty;
    
    [CsvColumn("Price")]
    public decimal Price { get; set; }
    
    [CsvColumn("Category")]
    public string Category { get; set; } = string.Empty;

    public string? RowId { get; set; }
}

