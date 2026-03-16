using BulkSharp.Core.Exceptions;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BulkSharp.UnitTests.Services;

[Trait("Category", "Unit")]
public class SignalCompletionHandlerTests
{
    [Fact]
    public async Task WaitForCompletionAsync_CompletesWhenSignalReceived()
    {
        var signalService = new BulkStepSignalService();
        var recordRepoMock = new Mock<IBulkRowRecordRepository>();
        var handler = new SignalCompletionHandler(signalService, recordRepoMock.Object, NullLogger<SignalCompletionHandler>.Instance);

        var asyncStep = new Mock<IAsyncBulkStep<SignalTestMetadata, SignalTestRow>>();
        asyncStep.Setup(s => s.Name).Returns("TestStep");
        asyncStep.Setup(s => s.Timeout).Returns(TimeSpan.FromSeconds(10));
        asyncStep.Setup(s => s.CompletionMode).Returns(StepCompletionMode.Signal);
        asyncStep.Setup(s => s.GetSignalKey(It.IsAny<SignalTestRow>(), It.IsAny<SignalTestMetadata>()))
            .Returns("test-signal-key");

        var operationId = Guid.NewGuid();
        var record = new BulkRowRecord { StepName = "TestStep", BulkOperationId = operationId, RowNumber = 0 };
        var row = new SignalTestRow();
        var metadata = new SignalTestMetadata();

        handler.PrepareStatus(asyncStep.Object, row, metadata, record);
        var expectedKey = $"{operationId}:test-signal-key:0";
        record.SignalKey.Should().Be(expectedKey);

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            signalService.TrySignal(expectedKey);
        });

        await handler.WaitForCompletionAsync(asyncStep.Object, row, metadata, record, CancellationToken.None);
    }

    [Fact]
    public async Task WaitForCompletionAsync_ThrowsTimeoutWhenNoSignal()
    {
        var signalService = new BulkStepSignalService();
        var recordRepoMock = new Mock<IBulkRowRecordRepository>();
        var handler = new SignalCompletionHandler(signalService, recordRepoMock.Object, NullLogger<SignalCompletionHandler>.Instance);

        var asyncStep = new Mock<IAsyncBulkStep<SignalTestMetadata, SignalTestRow>>();
        asyncStep.Setup(s => s.Name).Returns("TestStep");
        asyncStep.Setup(s => s.Timeout).Returns(TimeSpan.FromMilliseconds(100));
        asyncStep.Setup(s => s.CompletionMode).Returns(StepCompletionMode.Signal);
        asyncStep.Setup(s => s.GetSignalKey(It.IsAny<SignalTestRow>(), It.IsAny<SignalTestMetadata>()))
            .Returns("no-signal-key");

        var record = new BulkRowRecord { StepName = "TestStep", BulkOperationId = Guid.NewGuid(), RowNumber = 0 };
        var row = new SignalTestRow();
        var metadata = new SignalTestMetadata();

        handler.PrepareStatus(asyncStep.Object, row, metadata, record);

        await Assert.ThrowsAsync<BulkStepTimeoutException>(() =>
            handler.WaitForCompletionAsync(asyncStep.Object, row, metadata, record, CancellationToken.None));
    }

    [Fact]
    public async Task WaitForCompletionAsync_WhenSignaledWithFailure_ThrowsBulkStepSignalFailureException()
    {
        var signalService = new BulkStepSignalService();
        var recordRepoMock = new Mock<IBulkRowRecordRepository>();
        var handler = new SignalCompletionHandler(signalService, recordRepoMock.Object, NullLogger<SignalCompletionHandler>.Instance);

        var asyncStep = new Mock<IAsyncBulkStep<SignalTestMetadata, SignalTestRow>>();
        asyncStep.Setup(s => s.Name).Returns("TestStep");
        asyncStep.Setup(s => s.Timeout).Returns(TimeSpan.FromSeconds(10));
        asyncStep.Setup(s => s.CompletionMode).Returns(StepCompletionMode.Signal);
        asyncStep.Setup(s => s.GetSignalKey(It.IsAny<SignalTestRow>(), It.IsAny<SignalTestMetadata>()))
            .Returns("fail-signal-key");

        var operationId = Guid.NewGuid();
        var record = new BulkRowRecord { StepName = "TestStep", BulkOperationId = operationId, RowNumber = 0 };
        var row = new SignalTestRow();
        var metadata = new SignalTestMetadata();

        handler.PrepareStatus(asyncStep.Object, row, metadata, record);
        var expectedKey = $"{operationId}:fail-signal-key:0";

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            signalService.TrySignalFailure(expectedKey, "External process failed: payment declined");
        });

        var ex = await Assert.ThrowsAsync<BulkStepSignalFailureException>(() =>
            handler.WaitForCompletionAsync(asyncStep.Object, row, metadata, record, CancellationToken.None));

        ex.Message.Should().Be("External process failed: payment declined");
        ex.SignalKey.Should().Be(expectedKey);
    }
}

[Trait("Category", "Unit")]
public class BulkStepSignalServiceTests
{
    [Fact]
    public void TrySignalFailure_WithRegisteredWaiter_SetsExceptionAndReturnsTrue()
    {
        var service = new BulkStepSignalService();
        var tcs = service.RegisterWaiter("key-1");

        var result = service.TrySignalFailure("key-1", "something broke");

        result.Should().BeTrue();
        tcs.Task.IsFaulted.Should().BeTrue();
        tcs.Task.Exception!.InnerException.Should().BeOfType<BulkStepSignalFailureException>()
            .Which.Message.Should().Be("something broke");
    }

    [Fact]
    public void TrySignalFailure_WithNoWaiter_ReturnsFalse()
    {
        var service = new BulkStepSignalService();
        service.TrySignalFailure("nonexistent", "error").Should().BeFalse();
    }
}

public class SignalTestMetadata : IBulkMetadata { }
public class SignalTestRow : IBulkRow { public string? RowId { get; set; } }
