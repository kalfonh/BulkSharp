using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BulkSharp.UnitTests.Events;

[Trait("Category", "Unit")]
public class BulkOperationEventDispatcherTests
{
    [Fact]
    public async Task Dispatch_CompletedEvent_CallsOnOperationCompleted()
    {
        var handler = new Mock<IBulkOperationEventHandler>();
        handler.Setup(h => h.OnOperationCompletedAsync(It.IsAny<BulkOperationCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new BulkOperationEventDispatcher(
            new[] { handler.Object },
            NullLogger<BulkOperationEventDispatcher>.Instance);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = Guid.NewGuid(),
            OperationName = "test",
            Status = BulkOperationStatus.Completed,
            TotalRows = 10,
            SuccessfulRows = 10
        };

        await dispatcher.DispatchAsync(e);

        handler.Verify(h => h.OnOperationCompletedAsync(e, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_NoHandlers_DoesNotThrow()
    {
        var dispatcher = new BulkOperationEventDispatcher(
            Enumerable.Empty<IBulkOperationEventHandler>(),
            NullLogger<BulkOperationEventDispatcher>.Instance);

        await dispatcher.DispatchAsync(new BulkOperationCompletedEvent
        {
            OperationId = Guid.NewGuid(),
            OperationName = "test",
            Status = BulkOperationStatus.Completed
        });
    }

    [Fact]
    public async Task Dispatch_HandlerThrows_LogsAndContinues()
    {
        var failingHandler = new Mock<IBulkOperationEventHandler>();
        failingHandler.Setup(h => h.OnOperationCompletedAsync(It.IsAny<BulkOperationCompletedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("handler error"));

        var successHandler = new Mock<IBulkOperationEventHandler>();
        successHandler.Setup(h => h.OnOperationCompletedAsync(It.IsAny<BulkOperationCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new BulkOperationEventDispatcher(
            new[] { failingHandler.Object, successHandler.Object },
            NullLogger<BulkOperationEventDispatcher>.Instance);

        await dispatcher.DispatchAsync(new BulkOperationCompletedEvent
        {
            OperationId = Guid.NewGuid(),
            OperationName = "test",
            Status = BulkOperationStatus.Completed
        });

        successHandler.Verify(h => h.OnOperationCompletedAsync(It.IsAny<BulkOperationCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_MultipleHandlers_AllCalled()
    {
        var handlers = Enumerable.Range(0, 3)
            .Select(_ =>
            {
                var h = new Mock<IBulkOperationEventHandler>();
                h.Setup(x => x.OnOperationFailedAsync(It.IsAny<BulkOperationFailedEvent>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                return h;
            })
            .ToList();

        var dispatcher = new BulkOperationEventDispatcher(
            handlers.Select(h => h.Object),
            NullLogger<BulkOperationEventDispatcher>.Instance);

        await dispatcher.DispatchAsync(new BulkOperationFailedEvent
        {
            OperationId = Guid.NewGuid(),
            OperationName = "test",
            Status = BulkOperationStatus.Failed,
            ErrorMessage = "error"
        });

        foreach (var handler in handlers)
            handler.Verify(h => h.OnOperationFailedAsync(It.IsAny<BulkOperationFailedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
