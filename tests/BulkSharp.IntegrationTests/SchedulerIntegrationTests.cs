using BulkSharp;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class SchedulerIntegrationTests
{
    [Fact]
    public async Task JobCreation_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder.UseFileStorage(fs => fs.UseInMemory())
                   .UseMetadataStorage(ms => ms.UseInMemory())
                   .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();

        var csvContent = "Name,Email,Age\nTest User,test@example.com,25";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, "test.csv", metadata, "admin");

        // Assert
        var job = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(job);
        Assert.Equal("test-operation", job.OperationName);
        Assert.Equal("admin", job.CreatedBy);
    }

    [Fact]
    public async Task MultipleJobCreation_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder.UseFileStorage(fs => fs.UseInMemory())
                   .UseMetadataStorage(ms => ms.UseInMemory())
                   .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();

        var csvContent = "Name,Email,Age\nUser1,user1@test.com,25\nUser2,user2@test.com,30";
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act - Create multiple jobs
        var jobIds = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            var jobId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, $"test{i}.csv", metadata, "admin");
            jobIds.Add(jobId);
        }

        // Assert
        Assert.Equal(3, jobIds.Count);
        foreach (var jobId in jobIds)
        {
            var job = await operationService.GetBulkOperationAsync(jobId);
            Assert.NotNull(job);
            Assert.Equal("test-operation", job.OperationName);
        }
    }
}
