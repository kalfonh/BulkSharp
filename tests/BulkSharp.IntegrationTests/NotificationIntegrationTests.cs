using BulkSharp.Core.Abstractions.Notifications;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Notifications;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class NotificationIntegrationTests
{
    [Fact]
    public async Task ProcessOperation_WithNotifications_ChannelReceivesNotification()
    {
        CapturingNotificationChannel.Reset();
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate())
                .AddNotificationChannel<CapturingNotificationChannel>();
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name,Email,Age\nAlice,alice@test.com,30\nBob,bob@test.com,25";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };
        var notifications = new NotificationOptions
        {
            Recipients = [new("email", "admin@example.com") { Triggers = NotificationTrigger.OnCompletion }]
        };

        var operationId = await operationService.CreateBulkOperationAsync(
            "test-operation", csvStream, "test.csv", metadata, "admin", notifications);
        await processor.ProcessOperationAsync(operationId);

        var captured = CapturingNotificationChannel.Captured;
        captured.Should().HaveCount(1);
        captured[0].Recipient.Target.Should().Be("admin@example.com");
        captured[0].Event.Should().BeOfType<BulkOperationCompletedEvent>();
        captured[0].Operation.Id.Should().Be(operationId);
    }

    [Fact]
    public async Task ProcessOperation_WithoutNotifications_ChannelNotCalled()
    {
        CapturingNotificationChannel.Reset();
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate())
                .AddNotificationChannel<CapturingNotificationChannel>();
        });
        services.AddScoped<TestBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name,Email,Age\nAlice,alice@test.com,30";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestMetadata { RequestedBy = "admin", Department = "IT" };

        var operationId = await operationService.CreateBulkOperationAsync(
            "test-operation", csvStream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(operationId);

        CapturingNotificationChannel.Captured.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessOperation_FailureWithOnFailureTrigger_ChannelReceivesNotification()
    {
        CapturingNotificationChannel.Reset();
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate())
                .AddNotificationChannel<CapturingNotificationChannel>();
        });
        services.AddScoped<FailingBulkOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name,Email,Age\nAlice,alice@test.com,30";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var notifications = new NotificationOptions
        {
            Recipients = [new("email", "ops@example.com") { Triggers = NotificationTrigger.OnFailure }]
        };

        var operationId = await operationService.CreateBulkOperationAsync(
            "failing-operation", csvStream, "test.csv", "{}", "admin", notifications);
        await processor.ProcessOperationAsync(operationId);

        var captured = CapturingNotificationChannel.Captured;
        captured.Should().HaveCount(1);
        captured[0].Event.Should().BeOfType<BulkOperationFailedEvent>();
    }
}

/// <summary>
/// Test notification channel that captures all sent notifications via a static list.
/// Static storage is required because DI creates new scoped instances per resolution;
/// using a shared static list allows assertions regardless of which instance SendAsync is called on.
/// Callers must invoke <see cref="Reset"/> at the start of each test to isolate state.
/// </summary>
public class CapturingNotificationChannel : IBulkNotificationChannel
{
    // Static so all DI-scoped instances share the same captured list across scope boundaries.
    private static readonly List<BulkNotificationContext> _captured = [];

    public string ChannelName => "email";

    /// <summary>All notifications sent across all instances since the last <see cref="Reset"/> call.</summary>
    public static IReadOnlyList<BulkNotificationContext> Captured
    {
        get
        {
            lock (_captured) return _captured.ToList();
        }
    }

    /// <summary>Clears captured notifications. Call at the start of each test.</summary>
    public static void Reset()
    {
        lock (_captured) _captured.Clear();
    }

    public Task SendAsync(BulkNotificationContext context, CancellationToken cancellationToken = default)
    {
        lock (_captured) _captured.Add(context);
        return Task.CompletedTask;
    }
}

[BulkOperation("failing-operation")]
public class FailingBulkOperation : IBulkRowOperation<FailingMetadata, TestCsvRow>
{
    // Throw during metadata validation so the operation transitions to Failed.
    // Row-level exceptions are caught per-row and produce CompletedWithErrors, not Failed.
    public Task ValidateMetadataAsync(FailingMetadata metadata, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated metadata validation failure");

    public Task ValidateRowAsync(TestCsvRow row, FailingMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ProcessRowAsync(TestCsvRow row, FailingMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public class FailingMetadata : IBulkMetadata
{
}
