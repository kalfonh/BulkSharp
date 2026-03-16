using System;
using System.Threading;
using System.Threading.Tasks;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Abstractions;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class BulkStepExecutorTests
{
    private readonly Mock<IBulkRowRecordRepository> _recordRepoMock = new();
    private readonly Mock<IBulkStepRecordManager> _recordManagerMock = new();
    private readonly BulkStepExecutorService _executor;

    public BulkStepExecutorTests()
    {
        var signalService = new BulkStepSignalService();
        var pollingHandler = new PollingCompletionHandler(NullLogger<PollingCompletionHandler>.Instance);
        var signalHandler = new SignalCompletionHandler(signalService, _recordRepoMock.Object, NullLogger<SignalCompletionHandler>.Instance);
        IAsyncStepCompletionHandler[] handlers = [pollingHandler, signalHandler];
        _executor = new BulkStepExecutorService(
            NullLogger<BulkStepExecutorService>.Instance,
            signalService, handlers);
    }

    [Fact]
    public async Task ExecuteStepAsync_SuccessfulExecution_ReturnsCompletedStep()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("TestStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(3);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata());
        stepMock.Verify(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteStepAsync_FailureWithRetries_RetriesAndSucceeds()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("TestStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(2);
        stepMock.SetupSequence(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("First failure"))
            .ThrowsAsync(new Exception("Second failure"))
            .Returns(Task.CompletedTask);

        await _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata());
        stepMock.Verify(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteStepAsync_ExceedsMaxRetries_ReturnsFailed()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("TestStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(1);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Persistent failure"));

        await Assert.ThrowsAsync<Exception>(() => _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata()));
        stepMock.Verify(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteStepAsync_SyncStep_TracksStateViaRecordManager()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("TestStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(0);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var operationId = Guid.NewGuid();
        var record = BulkRowRecord.CreateStep(operationId, 1, null, "TestStep", 0);

        await _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata(), record, _recordManagerMock.Object);

        // Record should be marked completed, then UpdateAsync called
        Assert.Equal(RowRecordState.Completed, record.State);
        _recordManagerMock.Verify(m => m.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteStepAsync_SyncStep_FailureTracksStateViaRecordManager()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("FailStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(0);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("step failed"));

        var operationId = Guid.NewGuid();
        var record = BulkRowRecord.CreateStep(operationId, 1, null, "FailStep", 0);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata(), record, _recordManagerMock.Object));

        // Failed status tracked via record manager
        Assert.Equal(RowRecordState.Failed, record.State);
        _recordManagerMock.Verify(m => m.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
    }
}

[Trait("Category", "Unit")]
public class TestMetadata : IBulkMetadata { }

[Trait("Category", "Unit")]
public class TestRow : IBulkRow
{
    public string? GetId() => Guid.NewGuid().ToString();
    public string? RowId { get; set; }
}
