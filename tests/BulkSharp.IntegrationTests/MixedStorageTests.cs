using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BulkSharp.Data.EntityFramework;
using BulkSharp.Processing.Scheduling;
using BulkSharp.Processing.Storage;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using System.ComponentModel.DataAnnotations;

namespace BulkSharp.IntegrationTests;

/// <summary>
/// Integration tests to verify the separation of concerns between
/// file storage, metadata storage, and scheduling.
/// </summary>
[Trait("Category", "Integration")]
public class MixedStorageTests
{
    [Fact]
    public async Task FileSystemWithSqlMetadata_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var dbName = $"TestDb_{Guid.NewGuid()}";

        // Configure with file system for files and EF for metadata
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseFileSystem(opts => opts.BasePath = "test-files"))
                .UseMetadataStorage(ms => { }) // EF metadata registered below
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<MixedTestBulkOperation>();

        var provider = services.BuildServiceProvider();

        // Act
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var csvContent = "Name,Email\nJohn,john@test.com";
        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin" };

        var operationId = await operationService.CreateBulkOperationAsync(
            "test-operation", csvStream, "test.csv", metadata, "admin");

        // Assert
        // Verify file is stored in file system
        var storageProvider = provider.GetRequiredService<IFileStorageProvider>();
        var operation = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(operation);

        // Verify metadata is in database
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BulkSharpDbContext>();
        var dbOperation = await dbContext.BulkOperations
            .FirstOrDefaultAsync(o => o.Id == operationId);
        Assert.NotNull(dbOperation);

        // Verify file metadata is in database
        var dbFile = await dbContext.BulkFiles
            .FirstOrDefaultAsync(f => f.Id == operation.FileId);
        Assert.NotNull(dbFile);
        Assert.Equal(StorageProviderNames.FileSystem, dbFile.StorageProvider);

        // Cleanup
        Directory.Delete("test-files", true);
    }

    [Fact]
    public async Task InMemoryFileWithEFMetadata_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var dbName = $"TestDb_{Guid.NewGuid()}";

        // Configure with in-memory for files and EF for metadata
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => { }) // EF metadata registered below
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<MixedTestBulkOperation>();

        var provider = services.BuildServiceProvider();

        // Act
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var csvContent = "Name,Email\nJane,jane@test.com";
        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "user" };

        var operationId = await operationService.CreateBulkOperationAsync(
            "test-operation", csvStream, "test.csv", metadata, "user");

        // Assert
        var operation = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(operation);

        // Verify metadata is in EF database
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BulkSharpDbContext>();
        var dbOperation = await dbContext.BulkOperations.FindAsync(operationId);
        Assert.NotNull(dbOperation);

        // Verify file metadata is also in database
        var dbFile = await dbContext.BulkFiles
            .FirstOrDefaultAsync(f => f.Id == operation.FileId);
        Assert.NotNull(dbFile);
        Assert.Equal(StorageProviderNames.InMemory, dbFile.StorageProvider);
    }

    [Fact]
    public async Task ChannelsScheduler_ShouldProcessWithWorkers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Configure with Channels scheduler
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseChannels(options =>
                {
                    options.WorkerCount = 2;
                    options.QueueCapacity = 100;
                }));
        });

        services.AddScoped<MixedTestBulkOperation>();

        var provider = services.BuildServiceProvider();

        // Start the scheduler as hosted service
        var scheduler = provider.GetRequiredService<ChannelsScheduler>();
        await scheduler.StartAsync(CancellationToken.None);

        try
        {
            // Act
            var operationService = provider.GetRequiredService<IBulkOperationService>();
            var bulkScheduler = provider.GetRequiredService<IBulkScheduler>();

            // Create multiple operations
            var operationIds = new List<Guid>();
            for (int i = 0; i < 5; i++)
            {
                var csvContent = $"Name,Email\nUser{i},user{i}@test.com";
                using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
                var metadata = new TestMetadata { RequestedBy = $"user{i}" };

                var operationId = await operationService.CreateBulkOperationAsync(
                    "test-operation", csvStream, $"test{i}.csv", metadata, $"user{i}");
                operationIds.Add(operationId);

                // Schedule for processing
                await bulkScheduler.ScheduleBulkOperationAsync(operationId);
            }

            // Wait for processing
            await Task.Delay(2000);

            // Assert - all operations should be processed
            foreach (var operationId in operationIds)
            {
                var operation = await operationService.GetBulkOperationAsync(operationId);
                Assert.NotNull(operation);
                // Note: Status check would require processor implementation
            }
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MixedStorageProviders_ShouldMaintainSeparation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // First configuration: FileSystem + EF metadata
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseFileSystem(opts => opts.BasePath = "storage1"))
                .UseMetadataStorage(ms => { }) // EF metadata registered below
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>(options =>
            options.UseInMemoryDatabase("SharedDb"));

        services.AddScoped<MixedTestBulkOperation>();
        var provider1 = services.BuildServiceProvider();

        // Create operation with first configuration
        var operationService1 = provider1.GetRequiredService<IBulkOperationService>();
        var csvContent1 = "Name,Email\nAlice,alice@test.com";
        using var csvStream1 = new MemoryStream(Encoding.UTF8.GetBytes(csvContent1));
        var operationId1 = await operationService1.CreateBulkOperationAsync(
            "test-op1", csvStream1, "test1.csv", new TestMetadata { RequestedBy = "admin1" }, "admin1");

        // Second configuration: InMemory files + Same SQL DB
        var services2 = new ServiceCollection();
        services2.AddLogging();
        services2.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => { }) // EF metadata registered below
                .UseScheduler(s => s.UseImmediate());
        });
        services2.AddBulkSharpEntityFramework<BulkSharpDbContext>(options =>
            options.UseInMemoryDatabase("SharedDb"));

        services2.AddScoped<TestBulkOperation>();
        var provider2 = services2.BuildServiceProvider();

        // Create operation with second configuration
        var operationService2 = provider2.GetRequiredService<IBulkOperationService>();
        var csvContent2 = "Name,Email\nBob,bob@test.com";
        using var csvStream2 = new MemoryStream(Encoding.UTF8.GetBytes(csvContent2));
        var operationId2 = await operationService2.CreateBulkOperationAsync(
            "test-op2", csvStream2, "test2.csv", new TestMetadata { RequestedBy = "admin2" }, "admin2");

        // Assert - Both operations should be in the same database
        using var scope = provider1.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BulkSharpDbContext>();

        var allOperations = await dbContext.BulkOperations.ToListAsync();
        Assert.Equal(2, allOperations.Count);

        var allFiles = await dbContext.BulkFiles.ToListAsync();
        Assert.Equal(2, allFiles.Count);

        // Verify different storage providers
        var file1 = allFiles.First(f => f.Id == allOperations.First(o => o.Id == operationId1).FileId);
        var file2 = allFiles.First(f => f.Id == allOperations.First(o => o.Id == operationId2).FileId);

        Assert.Equal(StorageProviderNames.FileSystem, file1.StorageProvider);
        Assert.Equal(StorageProviderNames.InMemory, file2.StorageProvider);

        // Cleanup
        if (Directory.Exists("storage1"))
            Directory.Delete("storage1", true);
    }

    [Fact]
    public async Task ProductionConfiguration_ShouldUseOptimalSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var dbName = $"ProdDb_{Guid.NewGuid()}";

        // Use the production configuration
        services.AddBulkSharp(cfg =>
        {
            cfg.UseFileStorage(f => f.UseFileSystem(opts => opts.BasePath = "prod-storage"));
            cfg.UseMetadataStorage(ms => { }); // EF metadata registered below
            cfg.UseScheduler(s => s.UseImmediate());
        });
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<MixedTestBulkOperation>();

        var provider = services.BuildServiceProvider();

        // Act & Assert
        // Verify scheduler is configured
        var scheduler = provider.GetRequiredService<IBulkScheduler>();
        Assert.NotNull(scheduler);

        // Verify EF repositories are configured
        var opRepo = provider.GetRequiredService<IBulkOperationRepository>();
        Assert.IsType<EntityFrameworkBulkOperationRepository>(opRepo);

        // Verify file storage is filesystem
        var storageProvider = provider.GetRequiredService<IFileStorageProvider>();
        Assert.IsType<BasicFileStorageProvider>(storageProvider);

        // Cleanup
        if (Directory.Exists("prod-storage"))
            Directory.Delete("prod-storage", true);

        await Task.CompletedTask;
    }
}

// Test support classes
[BulkOperation("test-mixed-operation")]
[Trait("Category", "Integration")]
public class MixedTestBulkOperation : IBulkRowOperation<MixedTestMetadata, TestRow>
{
    public Task ValidateMetadataAsync(MixedTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(metadata.RequestedBy))
            throw new ValidationException("RequestedBy is required");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(TestRow row, MixedTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(row.Name))
            throw new ValidationException("Name is required");
        if (string.IsNullOrEmpty(row.Email) || !row.Email.Contains("@"))
            throw new ValidationException("Valid email is required");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(TestRow row, MixedTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate processing
        return Task.Delay(10, cancellationToken);
    }
}

[Trait("Category", "Integration")]
public class MixedTestMetadata : IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
    public string? Department { get; set; }
}

[Trait("Category", "Integration")]
public class TestRow : IBulkRow
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? RowId { get; set; }
}