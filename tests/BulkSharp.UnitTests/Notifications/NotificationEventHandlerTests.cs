using System.Text.Json;
using BulkSharp.Core.Abstractions.Notifications;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Notifications;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace BulkSharp.UnitTests.Notifications;

[Trait("Category", "Unit")]
public class NotificationEventHandlerTests
{
    private readonly Mock<IBulkNotificationChannel> _emailChannel;
    private readonly Mock<IBulkOperationRepository> _operationRepo;
    private readonly NotificationEventHandler _handler;

    public NotificationEventHandlerTests()
    {
        _emailChannel = new Mock<IBulkNotificationChannel>();
        _emailChannel.Setup(c => c.ChannelName).Returns("email");
        _emailChannel.Setup(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _operationRepo = new Mock<IBulkOperationRepository>();

        _handler = new NotificationEventHandler(
            new[] { _emailChannel.Object },
            _operationRepo.Object,
            NullLogger<NotificationEventHandler>.Instance);
    }

    [Fact]
    public async Task OnOperationCompleted_WithMatchingTrigger_SendsNotification()
    {
        var operation = CreateOperation(NotificationTrigger.OnCompletion);
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Completed,
            TotalRows = 10,
            SuccessfulRows = 10
        };

        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(
            It.Is<BulkNotificationContext>(ctx =>
                ctx.Event == e &&
                ctx.Operation == operation &&
                ctx.Recipient.Target == "test@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnOperationCompleted_WithNonMatchingTrigger_DoesNotSend()
    {
        var operation = CreateOperation(NotificationTrigger.OnFailure);
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Completed,
            TotalRows = 10,
            SuccessfulRows = 10
        };

        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnOperationFailed_WithMatchingTrigger_SendsNotification()
    {
        var operation = CreateOperation(NotificationTrigger.OnFailure);
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationFailedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Failed,
            ErrorMessage = "Something went wrong"
        };

        await _handler.OnOperationFailedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(
            It.Is<BulkNotificationContext>(ctx => ctx.Event == e),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnOperationCompleted_WithCompletedWithErrors_MatchesCorrectTrigger()
    {
        var operation = CreateOperation(NotificationTrigger.OnCompletionWithErrors);
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.CompletedWithErrors,
            TotalRows = 10,
            SuccessfulRows = 8,
            FailedRows = 2
        };

        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnOperationCompleted_NoNotificationOptions_DoesNotSend()
    {
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test",
            NotificationOptionsJson = null
        };
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Completed
        };

        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnOperationCompleted_UnknownChannel_LogsWarningAndContinues()
    {
        var options = new NotificationOptions
        {
            Recipients = [new("slack", "#ops") { Triggers = NotificationTrigger.OnCompletion }]
        };
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test",
            NotificationOptionsJson = JsonSerializer.Serialize(options)
        };
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Completed
        };

        // Should not throw -- unknown channels are logged and skipped
        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnOperationCompleted_ChannelThrows_DoesNotPropagate()
    {
        var operation = CreateOperation(NotificationTrigger.OnCompletion);
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _emailChannel.Setup(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Completed
        };

        // Should not throw
        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);
    }

    [Fact]
    public async Task OnOperationCompleted_MultipleRecipients_SendsToAll()
    {
        var options = new NotificationOptions
        {
            Recipients =
            [
                new("email", "alice@example.com") { Triggers = NotificationTrigger.OnCompletion },
                new("email", "bob@example.com") { Triggers = NotificationTrigger.OnCompletion }
            ]
        };
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test",
            NotificationOptionsJson = JsonSerializer.Serialize(options)
        };
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationCompletedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Completed
        };

        await _handler.OnOperationCompletedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task OnStatusChanged_WithOnStatusChangeTrigger_SendsNotification()
    {
        var operation = CreateOperation(NotificationTrigger.OnStatusChange);
        _operationRepo.Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var e = new BulkOperationStatusChangedEvent
        {
            OperationId = operation.Id,
            OperationName = "test",
            Status = BulkOperationStatus.Running,
            PreviousStatus = BulkOperationStatus.Validating
        };

        await _handler.OnStatusChangedAsync(e, CancellationToken.None);

        _emailChannel.Verify(c => c.SendAsync(It.IsAny<BulkNotificationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private BulkOperation CreateOperation(NotificationTrigger trigger)
    {
        var options = new NotificationOptions
        {
            Recipients = [new("email", "test@example.com") { Triggers = trigger }]
        };
        return new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test",
            NotificationOptionsJson = JsonSerializer.Serialize(options)
        };
    }
}
