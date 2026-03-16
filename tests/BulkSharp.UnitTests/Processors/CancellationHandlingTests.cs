using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Discovery;
using BulkSharp.Processing.Processors;

namespace BulkSharp.UnitTests.Processors;

[Trait("Category", "Unit")]
public class CancellationHandlingTests
{
    private readonly Mock<IBulkOperationRepository> _operationRepositoryMock;
    private readonly Mock<IBulkOperationDiscovery> _operationDiscoveryMock;
    private readonly Mock<IBulkOperationEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<BulkOperationProcessor>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly BulkOperationProcessor _processor;

    public CancellationHandlingTests()
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
    public async Task ProcessOperationAsync_WhenCancelled_ShouldMarkOperationCancelled()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test-operation",
            Status = BulkOperationStatus.Pending,
            MetadataJson = "{}"
        };

        var operationType = typeof(CancellationFakeOperation);
        var typedProcessorMock = new Mock<ITypedBulkOperationProcessor<CancellationFakeOperation, CancellationFakeMetadata, CancellationFakeRow>>();
        typedProcessorMock
            .Setup(x => x.ProcessOperationAsync(
                It.IsAny<BulkOperation>(),
                It.IsAny<CancellationFakeOperation>(),
                It.IsAny<CancellationFakeMetadata>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        _operationRepositoryMock
            .Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _operationDiscoveryMock
            .Setup(x => x.GetOperation("test-operation"))
            .Returns(new BulkOperationInfo
            {
                Name = "test-operation",
                OperationType = operationType,
                MetadataType = typeof(CancellationFakeMetadata),
                RowType = typeof(CancellationFakeRow)
            });

        _serviceProviderMock
            .Setup(x => x.GetService(operationType))
            .Returns(new CancellationFakeOperation());

        _serviceProviderMock
            .Setup(x => x.GetService(typeof(ITypedBulkOperationProcessor<CancellationFakeOperation, CancellationFakeMetadata, CancellationFakeRow>)))
            .Returns(typedProcessorMock.Object);

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
    public async Task ProcessOperationAsync_WhenCancelled_ShouldLogWarningNotError()
    {
        // Arrange
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test-operation",
            Status = BulkOperationStatus.Pending,
            MetadataJson = "{}"
        };

        var operationType = typeof(CancellationFakeOperation);
        var typedProcessorMock = new Mock<ITypedBulkOperationProcessor<CancellationFakeOperation, CancellationFakeMetadata, CancellationFakeRow>>();
        typedProcessorMock
            .Setup(x => x.ProcessOperationAsync(
                It.IsAny<BulkOperation>(),
                It.IsAny<CancellationFakeOperation>(),
                It.IsAny<CancellationFakeMetadata>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        _operationRepositoryMock
            .Setup(x => x.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _operationDiscoveryMock
            .Setup(x => x.GetOperation("test-operation"))
            .Returns(new BulkOperationInfo
            {
                Name = "test-operation",
                OperationType = operationType,
                MetadataType = typeof(CancellationFakeMetadata),
                RowType = typeof(CancellationFakeRow)
            });

        _serviceProviderMock
            .Setup(x => x.GetService(operationType))
            .Returns(new CancellationFakeOperation());

        _serviceProviderMock
            .Setup(x => x.GetService(typeof(ITypedBulkOperationProcessor<CancellationFakeOperation, CancellationFakeMetadata, CancellationFakeRow>)))
            .Returns(typedProcessorMock.Object);

        // Act
        await _processor.ProcessOperationAsync(operation.Id);

        // Assert - should log Warning, not Error
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cancelled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<OperationCanceledException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

}

// Fakes must be non-nested and public so Castle.DynamicProxy can access them when mocking generic interfaces
[Trait("Category", "Unit")]
public class CancellationFakeMetadata : IBulkMetadata
{
}

[Trait("Category", "Unit")]
public class CancellationFakeRow : IBulkRow
{
    public string? RowId { get; set; }
}

[Trait("Category", "Unit")]
public class CancellationFakeOperation : IBulkRowOperation<CancellationFakeMetadata, CancellationFakeRow>
{
    public Task ValidateMetadataAsync(CancellationFakeMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ValidateRowAsync(CancellationFakeRow row, CancellationFakeMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ProcessRowAsync(CancellationFakeRow row, CancellationFakeMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
