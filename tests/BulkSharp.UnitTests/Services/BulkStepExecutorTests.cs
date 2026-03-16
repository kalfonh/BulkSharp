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
    private readonly Mock<IRowRecordFlushService> _flushServiceMock = new();
    private readonly Mock<IRowRecordPersistenceProvider> _persistenceProviderMock = new();
    private readonly Mock<IRowRecordPersistence> _batchedPersistenceMock = new();
    private readonly Mock<IRowRecordPersistence> _immediatePersistenceMock = new();
    private readonly BulkStepExecutorService _executor;

    public BulkStepExecutorTests()
    {
        // Sync steps get batched persistence, async steps get immediate persistence
        _persistenceProviderMock
            .Setup(p => p.GetPersistence(It.IsAny<IBulkStep<TestMetadata, TestRow>>()))
            .Returns(_batchedPersistenceMock.Object);

        var signalService = new BulkStepSignalService();
        var pollingHandler = new PollingCompletionHandler(NullLogger<PollingCompletionHandler>.Instance);
        var signalHandler = new SignalCompletionHandler(signalService, _recordRepoMock.Object, NullLogger<SignalCompletionHandler>.Instance);
        IAsyncStepCompletionHandler[] handlers = [pollingHandler, signalHandler];
        _executor = new BulkStepExecutorService(
            NullLogger<BulkStepExecutorService>.Instance, _persistenceProviderMock.Object,
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
    public async Task ExecuteStepAsync_SyncStep_BatchesViaFlushService()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("TestStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(0);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Capture state at call time since the same object is mutated after CreateAsync
        RowRecordState? stateAtCreate = null;
        _batchedPersistenceMock.Setup(p => p.CreateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()))
            .Callback<BulkRowRecord, CancellationToken>((s, _) => stateAtCreate = s.State)
            .Returns(Task.CompletedTask);

        RowRecordState? stateAtUpdate = null;
        _batchedPersistenceMock.Setup(p => p.UpdateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()))
            .Callback<BulkRowRecord, CancellationToken>((s, _) => stateAtUpdate = s.State)
            .Returns(Task.CompletedTask);

        var operationId = Guid.NewGuid();
        await _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata(), operationId, 1, 0);

        // Synchronous steps use persistence provider
        Assert.Equal(RowRecordState.Running, stateAtCreate);
        Assert.Equal(RowRecordState.Completed, stateAtUpdate);
        _batchedPersistenceMock.Verify(p => p.CreateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _batchedPersistenceMock.Verify(p => p.UpdateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Once);

        // Direct repo calls should NOT happen for sync steps
        _recordRepoMock.Verify(r => r.CreateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _recordRepoMock.Verify(r => r.UpdateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteStepAsync_SyncStep_FailureBatchesViaFlushService()
    {
        var stepMock = new Mock<IBulkStep<TestMetadata, TestRow>>();
        stepMock.SetupGet(s => s.Name).Returns("FailStep");
        stepMock.SetupGet(s => s.MaxRetries).Returns(0);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<TestRow>(), It.IsAny<TestMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("step failed"));

        // Capture state at call time
        RowRecordState? stateAtCreate = null;
        _batchedPersistenceMock.Setup(p => p.CreateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()))
            .Callback<BulkRowRecord, CancellationToken>((s, _) => stateAtCreate = s.State)
            .Returns(Task.CompletedTask);

        RowRecordState? stateAtUpdate = null;
        _batchedPersistenceMock.Setup(p => p.UpdateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()))
            .Callback<BulkRowRecord, CancellationToken>((s, _) => stateAtUpdate = s.State)
            .Returns(Task.CompletedTask);

        var operationId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _executor.ExecuteStepAsync(stepMock.Object, new TestRow(), new TestMetadata(), operationId, 1, 0));

        // Failed status through persistence provider
        Assert.Equal(RowRecordState.Running, stateAtCreate);
        Assert.Equal(RowRecordState.Failed, stateAtUpdate);
        _batchedPersistenceMock.Verify(p => p.CreateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _batchedPersistenceMock.Verify(p => p.UpdateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Once);

        // No direct repo calls
        _recordRepoMock.Verify(r => r.CreateAsync(It.IsAny<BulkRowRecord>(), It.IsAny<CancellationToken>()), Times.Never);
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
