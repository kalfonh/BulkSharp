using System.Text.Json;
using BulkSharp;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class RowTrackingAndEventTests
{
    [Fact]
    public async Task ProcessOperation_RowTracking_CreatesRowItemsWithCompletedStatus()
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

        var csvContent = "Name,Email,Age\nAlice,alice@test.com,30\nBob,bob@test.com,25\nCarol,carol@test.com,28";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        // Assert — validation records (StepIndex=-1) should all be Completed
        var result = await rowRecordRepo.QueryAsync(new BulkSharp.Core.Domain.Queries.BulkRowRecordQuery
        {
            OperationId = operationId,
            StepIndex = -1
        });
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(RowRecordState.Completed, item.State));
    }

    [Fact]
    public async Task ProcessOperation_EventHandler_CapturesExpectedEvents()
    {
        // Arrange
        var capturingHandler = new CapturingEventHandler();
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestBulkOperation>();
        services.AddSingleton<IBulkOperationEventHandler>(capturingHandler);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name,Email,Age\nAlice,alice@test.com,30\nBob,bob@test.com,25";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-operation", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        // Assert — expect: StatusChanged(Validating), StatusChanged(Running), Completed
        var statusEvents = capturingHandler.Events.OfType<BulkOperationStatusChangedEvent>().ToList();
        Assert.Equal(2, statusEvents.Count);
        Assert.Equal(BulkOperationStatus.Validating, statusEvents[0].Status);
        Assert.Equal(BulkOperationStatus.Running, statusEvents[1].Status);

        var completedEvents = capturingHandler.Events.OfType<BulkOperationCompletedEvent>().ToList();
        Assert.Single(completedEvents);
        Assert.Equal(BulkOperationStatus.Completed, completedEvents[0].Status);
        Assert.Equal(2, completedEvents[0].TotalRows);
        Assert.Equal(2, completedEvents[0].SuccessfulRows);
    }

    [Fact]
    public async Task ProcessOperation_TrackRowDataEnabled_RowItemsContainSerializedData()
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
        services.AddScoped<TestTrackedBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var rowRecordRepo = provider.GetRequiredService<IBulkRowRecordRepository>();

        var csvContent = "Name,Email,Age\nAlice,alice@test.com,30\nBob,bob@test.com,25\nCarol,carol@test.com,28";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        // Act
        var operationId = await operationService.CreateBulkOperationAsync("test-tracked", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        // Assert — validation records (StepIndex=-1) should contain serialized row data
        var result = await rowRecordRepo.QueryAsync(new BulkSharp.Core.Domain.Queries.BulkRowRecordQuery
        {
            OperationId = operationId,
            StepIndex = -1
        });
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, item =>
        {
            Assert.NotNull(item.RowData);
            // Verify it's valid JSON containing the row data
            var doc = JsonDocument.Parse(item.RowData!);
            Assert.True(doc.RootElement.TryGetProperty("Name", out _) || doc.RootElement.TryGetProperty("name", out _));
        });
    }
}

[Trait("Category", "Integration")]
public class CapturingEventHandler : IBulkOperationEventHandler
{
    public List<BulkOperationEvent> Events { get; } = new();

    public Task OnOperationCreatedAsync(BulkOperationCreatedEvent e, CancellationToken ct)
    {
        Events.Add(e);
        return Task.CompletedTask;
    }

    public Task OnStatusChangedAsync(BulkOperationStatusChangedEvent e, CancellationToken ct)
    {
        Events.Add(e);
        return Task.CompletedTask;
    }

    public Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        Events.Add(e);
        return Task.CompletedTask;
    }

    public Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct)
    {
        Events.Add(e);
        return Task.CompletedTask;
    }
}

[BulkOperation("test-tracked", TrackRowData = true)]
[Trait("Category", "Integration")]
public class TestTrackedBulkOperation : IBulkRowOperation<TestMetadata, TestCsvRow>
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
        return Task.CompletedTask;
    }
}
