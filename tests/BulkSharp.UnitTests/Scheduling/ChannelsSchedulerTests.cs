using BulkSharp.Core.Domain.Queries;
using BulkSharp.Processing.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class ChannelsSchedulerTests : IAsyncDisposable
{
    private readonly ChannelsScheduler _scheduler;
    private readonly Mock<IBulkOperationProcessor> _processorMock;
    private readonly ServiceProvider _serviceProvider;

    public ChannelsSchedulerTests()
    {
        _processorMock = new Mock<IBulkOperationProcessor>();

        var repositoryMock = new Mock<IBulkOperationRepository>();
        repositoryMock
            .Setup(r => r.QueryAsync(It.IsAny<BulkOperationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BulkOperation>
            {
                Items = Array.Empty<BulkOperation>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 1000
            });

        var services = new ServiceCollection();
        services.AddScoped(_ => _processorMock.Object);
        services.AddScoped(_ => repositoryMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        _scheduler = new ChannelsScheduler(
            NullLogger<ChannelsScheduler>.Instance,
            _serviceProvider,
            Options.Create(new ChannelsSchedulerOptions { WorkerCount = 1 }));
    }

    [Fact]
    public async Task ScheduleAndProcess_SingleOperation_CallsProcessor()
    {
        var operationId = Guid.NewGuid();
        var processed = new TaskCompletionSource<bool>();

        _processorMock
            .Setup(p => p.ProcessOperationAsync(operationId, It.IsAny<CancellationToken>()))
            .Callback(() => processed.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await _scheduler.StartAsync(CancellationToken.None);
        await _scheduler.ScheduleBulkOperationAsync(operationId);

        var completed = await Task.WhenAny(processed.Task, Task.Delay(5000));
        Assert.True(completed == processed.Task, "Processor was not called within timeout");

        _processorMock.Verify(p => p.ProcessOperationAsync(operationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOperation_BeforeProcessing_SkipsExecution()
    {
        var operationId = Guid.NewGuid();
        var otherOperationId = Guid.NewGuid();
        var otherProcessed = new TaskCompletionSource<bool>();

        // Set up a second operation that signals when processed — proves the worker ran
        _processorMock
            .Setup(p => p.ProcessOperationAsync(otherOperationId, It.IsAny<CancellationToken>()))
            .Callback(() => otherProcessed.TrySetResult(true))
            .Returns(Task.CompletedTask);

        // Cancel before starting the scheduler so nothing is processing yet
        await _scheduler.CancelBulkOperationAsync(operationId);
        await _scheduler.ScheduleBulkOperationAsync(operationId);
        await _scheduler.ScheduleBulkOperationAsync(otherOperationId);
        await _scheduler.StartAsync(CancellationToken.None);

        // Wait for the worker to process the second operation (proves it ran past the first)
        var completed = await Task.WhenAny(otherProcessed.Task, Task.Delay(10_000));
        Assert.True(completed == otherProcessed.Task, "Worker did not process sentinel operation within timeout");

        _processorMock.Verify(
            p => p.ProcessOperationAsync(operationId, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StopAsync_GracefulShutdown_CompletesWithinTimeout()
    {
        await _scheduler.StartAsync(CancellationToken.None);

        var stopTask = _scheduler.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(5000));

        Assert.True(completed == stopTask, "StopAsync did not complete within timeout");
    }

    [Fact]
    public void DoubleRegistrationGuard_CalledTwice_RegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharp(builder => builder
            .UseFileStorage(fs => fs.UseInMemory())
            .UseMetadataStorage(ms => ms.UseInMemory())
            .UseScheduler(s => s.UseChannels()));

        services.AddBulkSharp(builder => builder
            .UseFileStorage(fs => fs.UseInMemory())
            .UseMetadataStorage(ms => ms.UseInMemory())
            .UseScheduler(s => s.UseChannels()));

        var provider = services.BuildServiceProvider();
        var schedulers = provider.GetServices<IBulkScheduler>().ToList();

        Assert.Single(schedulers);
    }

    [Fact]
    public async Task PendingPollInterval_Null_NoPollStarted()
    {
        // Default options have PendingPollInterval = null — scheduler starts and stops cleanly
        await _scheduler.StartAsync(CancellationToken.None);
        await _scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PendingPollInterval_Set_PicksUpNewPendingOperations()
    {
        var operationId = Guid.NewGuid();
        var processed = new TaskCompletionSource<bool>();

        var repositoryMock = new Mock<IBulkOperationRepository>();
        var returnedOnce = false;
        repositoryMock
            .Setup(r => r.QueryAsync(It.IsAny<BulkOperationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // First call (startup recovery): empty.
                // Second call (first poll): return the operation.
                // Subsequent calls: empty (operation already picked up and processed).
                if (!returnedOnce)
                {
                    returnedOnce = true;
                    return new PagedResult<BulkOperation> { Items = Array.Empty<BulkOperation>(), TotalCount = 0, Page = 1, PageSize = 100 };
                }

                // Return the operation once, then stop (simulates status change after processing)
                var items = processed.Task.IsCompleted
                    ? Array.Empty<BulkOperation>()
                    : new[] { new BulkOperation { Id = operationId, Status = BulkOperationStatus.Pending } };

                return new PagedResult<BulkOperation>
                {
                    Items = items,
                    TotalCount = items.Length,
                    Page = 1,
                    PageSize = 100
                };
            });

        var processorMock = new Mock<IBulkOperationProcessor>();
        processorMock
            .Setup(p => p.ProcessOperationAsync(operationId, It.IsAny<CancellationToken>()))
            .Callback(() => processed.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => processorMock.Object);
        services.AddScoped(_ => repositoryMock.Object);
        using var sp = services.BuildServiceProvider();

        var scheduler = new ChannelsScheduler(
            NullLogger<ChannelsScheduler>.Instance,
            sp,
            Options.Create(new ChannelsSchedulerOptions
            {
                WorkerCount = 1,
                PendingPollInterval = TimeSpan.FromMilliseconds(100)
            }));

        try
        {
            await scheduler.StartAsync(CancellationToken.None);

            var completed = await Task.WhenAny(processed.Task, Task.Delay(5000));
            Assert.True(completed == processed.Task, "Poller did not pick up pending operation within timeout");

            processorMock.Verify(p => p.ProcessOperationAsync(operationId, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
            scheduler.Dispose();
        }
    }

    [Fact]
    public async Task PendingPollInterval_DuplicateNotEnqueuedTwice()
    {
        var operationId = Guid.NewGuid();
        var processCount = 0;
        var processingStarted = new TaskCompletionSource<bool>();
        var allowComplete = new TaskCompletionSource<bool>();

        var repositoryMock = new Mock<IBulkOperationRepository>();
        repositoryMock
            .Setup(r => r.QueryAsync(It.IsAny<BulkOperationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BulkOperation>
            {
                Items = new[] { new BulkOperation { Id = operationId, Status = BulkOperationStatus.Pending } },
                TotalCount = 1,
                Page = 1,
                PageSize = 100
            });

        var processorMock = new Mock<IBulkOperationProcessor>();
        processorMock
            .Setup(p => p.ProcessOperationAsync(operationId, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref processCount);
                processingStarted.TrySetResult(true);
            })
            .Returns(async () => await allowComplete.Task); // Hold until we release

        var services = new ServiceCollection();
        services.AddScoped(_ => processorMock.Object);
        services.AddScoped(_ => repositoryMock.Object);
        using var sp = services.BuildServiceProvider();

        var scheduler = new ChannelsScheduler(
            NullLogger<ChannelsScheduler>.Instance,
            sp,
            Options.Create(new ChannelsSchedulerOptions
            {
                WorkerCount = 1,
                PendingPollInterval = TimeSpan.FromMilliseconds(50)
            }));

        try
        {
            await scheduler.StartAsync(CancellationToken.None);

            // Wait for processing to start (proves poll picked it up)
            var started = await Task.WhenAny(processingStarted.Task, Task.Delay(10_000));
            Assert.True(started == processingStarted.Task, "Processing did not start within timeout");

            // Release the processor so the worker can complete
            allowComplete.SetResult(true);

            // The operation should only be enqueued once (dedup prevents re-enqueue while processing)
            Assert.Equal(1, processCount);
        }
        finally
        {
            allowComplete.TrySetResult(true);
            await scheduler.StopAsync(CancellationToken.None);
            scheduler.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.StopAsync(CancellationToken.None);
        _scheduler.Dispose();
        await _serviceProvider.DisposeAsync();
    }
}
