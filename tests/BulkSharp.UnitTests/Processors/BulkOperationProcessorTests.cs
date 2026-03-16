using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.DataFormats;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Processors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class BulkOperationProcessorTests
{
    private readonly Mock<IBulkOperationRepository> _operationRepositoryMock;
    private readonly Mock<IBulkOperationDiscovery> _operationDiscoveryMock;
    private readonly Mock<IBulkOperationEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<BulkOperationProcessor>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly BulkOperationProcessor _processor;

    public BulkOperationProcessorTests()
    {
        _operationRepositoryMock = new Mock<IBulkOperationRepository>();
        _operationDiscoveryMock = new Mock<IBulkOperationDiscovery>();
        _eventDispatcherMock = new Mock<IBulkOperationEventDispatcher>();
        _loggerMock = new Mock<ILogger<BulkOperationProcessor>>();
        _loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _serviceProviderMock = new Mock<IServiceProvider>();

        _processor = new BulkOperationProcessor(
            _operationRepositoryMock.Object,
            _operationDiscoveryMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object,
            _serviceProviderMock.Object);
    }

    [Fact]
    public async Task ProcessOperationAsync_WithNonExistentOperation_ShouldLogError()
    {
        // Arrange
        var operationId = Guid.NewGuid();
        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulkOperation?)null);

        // Act
        await _processor.ProcessOperationAsync(operationId);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessOperationAsync_WithUnregisteredOperation_ShouldUpdateStatusToFailed()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "TestOperation",
            Status = BulkOperationStatus.Pending
        };

        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Discovery mock returns null — operation type is not registered
        _operationDiscoveryMock.Setup(x => x.GetOperation("TestOperation"))
            .Returns((BulkSharp.Core.Domain.Discovery.BulkOperationInfo?)null);

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert — processor should catch the error and mark the operation as Failed
        operation.Status.Should().Be(BulkOperationStatus.Failed);
        operation.ErrorMessage.Should().Contain("not found");
        _operationRepositoryMock.Verify(
            x => x.UpdateAsync(
                It.Is<BulkOperation>(op => op.Status == BulkOperationStatus.Failed),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessOperationAsync_AlreadyCompleted_DoesNotReprocess()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "TestOperation",
            Status = BulkOperationStatus.Completed
        };

        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert — should not attempt any state transitions or updates
        _operationRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<BulkOperation>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _operationDiscoveryMock.Verify(
            x => x.GetOperation(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessOperationAsync_AlreadyRunning_DoesNotReprocess()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "TestOperation",
            Status = BulkOperationStatus.Running
        };

        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert
        _operationRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<BulkOperation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessOperationAsync_WhenCancelled_TransitionsToCancelled()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "TestOperation",
            Status = BulkOperationStatus.Pending
        };

        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _operationDiscoveryMock.Setup(x => x.GetOperation("TestOperation"))
            .Returns(new BulkSharp.Core.Domain.Discovery.BulkOperationInfo
            {
                Name = "TestOperation",
                OperationType = typeof(TestOperation),
                MetadataType = typeof(TestMetadata),
                RowType = typeof(TestRow)
            });

        // Resolve the operation type but throw cancellation when transitioning
        _serviceProviderMock.Setup(x => x.GetService(typeof(TestOperation)))
            .Throws(new OperationCanceledException());

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert
        operation.Status.Should().Be(BulkOperationStatus.Cancelled);
        _operationRepositoryMock.Verify(
            x => x.UpdateAsync(
                It.Is<BulkOperation>(op => op.Status == BulkOperationStatus.Cancelled),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessOperationAsync_WhenProcessorThrows_TransitionsToFailed()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "TestOperation",
            Status = BulkOperationStatus.Pending
        };

        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _operationDiscoveryMock.Setup(x => x.GetOperation("TestOperation"))
            .Returns(new BulkSharp.Core.Domain.Discovery.BulkOperationInfo
            {
                Name = "TestOperation",
                OperationType = typeof(TestOperation),
                MetadataType = typeof(TestMetadata),
                RowType = typeof(TestRow)
            });

        _serviceProviderMock.Setup(x => x.GetService(typeof(TestOperation)))
            .Throws(new InvalidOperationException("Test failure"));

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert
        operation.Status.Should().Be(BulkOperationStatus.Failed);
        operation.ErrorMessage.Should().Contain("Test failure");
    }

    [Fact]
    public async Task ProcessOperationAsync_WhenEventDispatchFails_StillPersistsTerminalState()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "TestOperation",
            Status = BulkOperationStatus.Pending
        };

        _operationRepositoryMock.Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _operationDiscoveryMock.Setup(x => x.GetOperation("TestOperation"))
            .Returns((BulkSharp.Core.Domain.Discovery.BulkOperationInfo?)null);

        // Event dispatcher throws — should not prevent state persistence
        _eventDispatcherMock
            .Setup(x => x.DispatchAsync(It.IsAny<BulkSharp.Core.Domain.Events.BulkOperationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Event dispatch failed"));

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert — operation should still be marked as Failed (from the missing operation info)
        operation.Status.Should().Be(BulkOperationStatus.Failed);
        _operationRepositoryMock.Verify(
            x => x.UpdateAsync(
                It.Is<BulkOperation>(op => op.Status == BulkOperationStatus.Failed),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // Test types for processor dispatch
    public class TestMetadata : IBulkMetadata
    {
        public Guid OperationId { get; set; }
    }

    public class TestRow : IBulkRow
    {
        public int RowNumber { get; set; }
        public string? RowId { get; set; }
    }

    [BulkOperation("TestOperation")]
    public class TestOperation : IBulkRowOperation<TestMetadata, TestRow>
    {
        public Task ValidateMetadataAsync(TestMetadata metadata, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ValidateRowAsync(TestRow row, TestMetadata metadata, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ProcessRowAsync(TestRow row, TestMetadata metadata, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
