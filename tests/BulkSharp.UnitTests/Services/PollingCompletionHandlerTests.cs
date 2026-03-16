using BulkSharp.Core.Exceptions;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BulkSharp.UnitTests.Services;

[Trait("Category", "Unit")]
public class PollingCompletionHandlerTests
{
    [Fact]
    public async Task WaitForCompletionAsync_CompletesWhenCheckReturnsTrue()
    {
        var handler = new PollingCompletionHandler(NullLogger<PollingCompletionHandler>.Instance);

        var asyncStep = new Mock<IAsyncBulkStep<PollingTestMetadata, PollingTestRow>>();
        asyncStep.Setup(s => s.Name).Returns("TestStep");
        asyncStep.Setup(s => s.Timeout).Returns(TimeSpan.FromSeconds(10));
        asyncStep.Setup(s => s.PollInterval).Returns(TimeSpan.FromMilliseconds(50));
        asyncStep.Setup(s => s.CompletionMode).Returns(StepCompletionMode.Polling);
        asyncStep.Setup(s => s.CheckCompletionAsync(It.IsAny<PollingTestRow>(), It.IsAny<PollingTestMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var status = new BulkRowRecord { StepName = "TestStep" };

        await handler.WaitForCompletionAsync(asyncStep.Object, new PollingTestRow(), new PollingTestMetadata(), status, CancellationToken.None);

        asyncStep.Verify(s => s.CheckCompletionAsync(It.IsAny<PollingTestRow>(), It.IsAny<PollingTestMetadata>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForCompletionAsync_ThrowsTimeoutWhenDeadlineExceeded()
    {
        var handler = new PollingCompletionHandler(NullLogger<PollingCompletionHandler>.Instance);

        var asyncStep = new Mock<IAsyncBulkStep<PollingTestMetadata, PollingTestRow>>();
        asyncStep.Setup(s => s.Name).Returns("TestStep");
        asyncStep.Setup(s => s.Timeout).Returns(TimeSpan.FromMilliseconds(100));
        asyncStep.Setup(s => s.PollInterval).Returns(TimeSpan.FromMilliseconds(30));
        asyncStep.Setup(s => s.CompletionMode).Returns(StepCompletionMode.Polling);
        asyncStep.Setup(s => s.CheckCompletionAsync(It.IsAny<PollingTestRow>(), It.IsAny<PollingTestMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var status = new BulkRowRecord { StepName = "TestStep" };

        await Assert.ThrowsAsync<BulkStepTimeoutException>(() =>
            handler.WaitForCompletionAsync(asyncStep.Object, new PollingTestRow(), new PollingTestMetadata(), status, CancellationToken.None));
    }
}

[Trait("Category", "Unit")]
public class PollingTestMetadata : IBulkMetadata { }
[Trait("Category", "Unit")]
public class PollingTestRow : IBulkRow { public string? RowId { get; set; } }
