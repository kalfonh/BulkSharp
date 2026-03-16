using System.Runtime.CompilerServices;
using System.Text;
using BulkSharp.Core.Abstractions.DataFormats;
using BulkSharp.Core.Configuration;
using BulkSharp.Core.Domain.Discovery;
using Microsoft.Extensions.Options;
using BulkSharp.Core.Domain.Files;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Exceptions;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class BulkOperationServiceTests
{
    private BulkOperationService CreateService(
        Mock<IBulkOperationRepository>? operationRepoMock = null,
        Mock<IBulkRowRecordRepository>? rowRecordRepoMock = null,
        Mock<IManagedStorageProvider>? storageMock = null,
        Mock<IBulkScheduler>? schedulerMock = null,
        Mock<IBulkOperationDiscovery>? discoveryMock = null,
        IServiceProvider? serviceProvider = null)
    {
        return new BulkOperationService(
            (operationRepoMock ?? new Mock<IBulkOperationRepository>()).Object,
            (rowRecordRepoMock ?? new Mock<IBulkRowRecordRepository>()).Object,
            (storageMock ?? new Mock<IManagedStorageProvider>()).Object,
            (schedulerMock ?? new Mock<IBulkScheduler>()).Object,
            (discoveryMock ?? new Mock<IBulkOperationDiscovery>()).Object,
            serviceProvider ?? new Mock<IServiceProvider>().Object,
            Options.Create(new BulkSharpOptions()),
            NullLogger<BulkOperationService>.Instance);
    }

    private static BulkOperationInfo CreateTestOpInfo() => new()
    {
        Name = "test-op",
        OperationType = typeof(ValidationFakeOperation),
        MetadataType = typeof(ValidationFakeMetadata),
        RowType = typeof(ValidationFakeRow),
        IsStepBased = false
    };

    private static Mock<IServiceProvider> CreateServiceProviderWithFileProcessor(
        IDataFormatProcessor<ValidationFakeRow>? processor = null)
    {
        var spMock = new Mock<IServiceProvider>();
        var factoryMock = new Mock<IDataFormatProcessorFactory<ValidationFakeRow>>();

        if (processor != null)
        {
            factoryMock
                .Setup(f => f.GetProcessor(It.IsAny<string>()))
                .Returns(processor);
        }

        spMock
            .Setup(sp => sp.GetService(typeof(IDataFormatProcessorFactory<ValidationFakeRow>)))
            .Returns(factoryMock.Object);

        // Return empty collections for validator enumerations
        spMock
            .Setup(sp => sp.GetService(typeof(IEnumerable<IBulkMetadataValidator<ValidationFakeMetadata>>)))
            .Returns(Array.Empty<IBulkMetadataValidator<ValidationFakeMetadata>>());

        return spMock;
    }

    private static Mock<IDataFormatProcessor<ValidationFakeRow>> CreateProcessorMock(
        params ValidationFakeRow[] rows)
    {
        var processorMock = new Mock<IDataFormatProcessor<ValidationFakeRow>>();
        processorMock
            .Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(rows));
        return processorMock;
    }

    private static async IAsyncEnumerable<ValidationFakeRow> ToAsyncEnumerable(
        ValidationFakeRow[] items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.CompletedTask;
        }
    }

    #region Validate tests

    [Fact]
    public async Task ValidateBulkOperationAsync_ValidOperationAndFile_ReturnsIsValidTrue()
    {
        // Arrange
        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock.Setup(d => d.GetOperation("test-op")).Returns(CreateTestOpInfo());

        var processorMock = CreateProcessorMock(new ValidationFakeRow());
        var spMock = CreateServiceProviderWithFileProcessor(processorMock.Object);

        var service = CreateService(discoveryMock: discoveryMock, serviceProvider: spMock.Object);
        using var stream = new MemoryStream("Name\nTest"u8.ToArray());

        // Act
        var result = await service.ValidateBulkOperationAsync(
            "test-op", "{}", stream, "test.csv");

        // Assert
        result.IsValid.Should().BeTrue();
        result.MetadataErrors.Should().BeEmpty();
        result.FileErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateBulkOperationAsync_UnknownOperationName_ReturnsMetadataError()
    {
        // Arrange
        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock.Setup(d => d.GetOperation("unknown-op")).Returns((BulkOperationInfo?)null);

        var service = CreateService(discoveryMock: discoveryMock);
        using var stream = new MemoryStream("Name\nTest"u8.ToArray());

        // Act
        var result = await service.ValidateBulkOperationAsync(
            "unknown-op", "{}", stream, "test.csv");

        // Assert
        result.IsValid.Should().BeFalse();
        result.MetadataErrors.Should().ContainSingle()
            .Which.Should().Contain("unknown-op");
    }

    [Fact]
    public async Task ValidateBulkOperationAsync_InvalidMetadataJson_StillRunsFileValidation()
    {
        // Arrange
        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock.Setup(d => d.GetOperation("test-op")).Returns(CreateTestOpInfo());

        var processorMock = CreateProcessorMock(new ValidationFakeRow());
        var spMock = CreateServiceProviderWithFileProcessor(processorMock.Object);

        var service = CreateService(discoveryMock: discoveryMock, serviceProvider: spMock.Object);
        using var stream = new MemoryStream("Name\nTest"u8.ToArray());

        // Act
        var result = await service.ValidateBulkOperationAsync(
            "test-op", "{{invalid json!!", stream, "test.csv");

        // Assert
        result.IsValid.Should().BeFalse();
        result.MetadataErrors.Should().ContainSingle()
            .Which.Should().Contain("Invalid metadata JSON");
        // File validation should still have run — no file errors for a valid file
        result.FileErrors.Should().BeEmpty();
        processorMock.Verify(
            p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateBulkOperationAsync_EmptyFile_ReturnsFileError()
    {
        // Arrange
        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock.Setup(d => d.GetOperation("test-op")).Returns(CreateTestOpInfo());

        // Processor that yields zero rows
        var processorMock = CreateProcessorMock();
        var spMock = CreateServiceProviderWithFileProcessor(processorMock.Object);

        var service = CreateService(discoveryMock: discoveryMock, serviceProvider: spMock.Object);
        using var stream = new MemoryStream("Name"u8.ToArray());

        // Act
        var result = await service.ValidateBulkOperationAsync(
            "test-op", "{}", stream, "test.csv");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FileErrors.Should().ContainSingle()
            .Which.Should().Contain("no data rows");
    }

    [Fact]
    public async Task ValidateBulkOperationAsync_MalformedFile_ReturnsFileError()
    {
        // Arrange
        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock.Setup(d => d.GetOperation("test-op")).Returns(CreateTestOpInfo());

        var processorMock = new Mock<IDataFormatProcessor<ValidationFakeRow>>();
        processorMock
            .Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable("Bad CSV format"));

        var spMock = CreateServiceProviderWithFileProcessor(processorMock.Object);

        var service = CreateService(discoveryMock: discoveryMock, serviceProvider: spMock.Object);
        using var stream = new MemoryStream("garbage"u8.ToArray());

        // Act
        var result = await service.ValidateBulkOperationAsync(
            "test-op", "{}", stream, "test.csv");

        // Assert
        result.IsValid.Should().BeFalse();
        result.FileErrors.Should().ContainSingle()
            .Which.Should().Contain("Bad CSV format");
    }

    private static async IAsyncEnumerable<ValidationFakeRow> ThrowingAsyncEnumerable(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException(message);
#pragma warning disable CS0162 // Unreachable code — required to satisfy IAsyncEnumerable<T> return type
        yield break;
#pragma warning restore CS0162
    }

    #endregion

    [Fact]
    public async Task CreateBulkOperationAsync_SchedulingFails_MarksOperationFailed()
    {
        // Arrange
        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<BulkOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulkOperation op, CancellationToken _) => op);

        var rowRecordRepoMock = new Mock<IBulkRowRecordRepository>();

        var storageMock = new Mock<IManagedStorageProvider>();
        storageMock
            .Setup(s => s.StoreFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkFile { Id = Guid.NewGuid() });

        var schedulerMock = new Mock<IBulkScheduler>();
        schedulerMock
            .Setup(s => s.ScheduleBulkOperationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Queue full"));

        var service = new BulkOperationService(
            operationRepoMock.Object,
            rowRecordRepoMock.Object,
            storageMock.Object,
            schedulerMock.Object,
            new Mock<IBulkOperationDiscovery>().Object,
            new Mock<IServiceProvider>().Object,
            Options.Create(new BulkSharpOptions()),
            NullLogger<BulkOperationService>.Instance);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Email\nTest,test@test.com"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateBulkOperationAsync("test-op", stream, "test.csv", new { }, "user"));

        // UpdateAsync is called twice: once to persist the FileId, once to mark as Failed
        operationRepoMock.Verify(r => r.UpdateAsync(
            It.Is<BulkOperation>(op => op.Status == BulkOperationStatus.Failed),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CreateBulkOperationAsync_Success_ReturnsOperationId()
    {
        // Arrange
        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<BulkOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulkOperation op, CancellationToken _) => op);

        var rowRecordRepoMock = new Mock<IBulkRowRecordRepository>();

        var storageMock = new Mock<IManagedStorageProvider>();
        storageMock
            .Setup(s => s.StoreFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkFile { Id = Guid.NewGuid() });

        var schedulerMock = new Mock<IBulkScheduler>();

        var service = new BulkOperationService(
            operationRepoMock.Object,
            rowRecordRepoMock.Object,
            storageMock.Object,
            schedulerMock.Object,
            new Mock<IBulkOperationDiscovery>().Object,
            new Mock<IServiceProvider>().Object,
            Options.Create(new BulkSharpOptions()),
            NullLogger<BulkOperationService>.Instance);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Email\nTest,test@test.com"));

        // Act
        var operationId = await service.CreateBulkOperationAsync("test-op", stream, "test.csv", new { }, "user");

        // Assert
        Assert.NotEqual(Guid.Empty, operationId);
        schedulerMock.Verify(s => s.ScheduleBulkOperationAsync(operationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelBulkOperationAsync_RunningOperation_MarksAsCancelled()
    {
        // Arrange
        var operation = new BulkOperation
        {
            OperationName = "test-op",
            Status = BulkOperationStatus.Pending
        };
        operation.MarkRunning();

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var schedulerMock = new Mock<IBulkScheduler>();
        var service = CreateService(operationRepoMock: operationRepoMock, schedulerMock: schedulerMock);

        // Act
        await service.CancelBulkOperationAsync(operation.Id);

        // Assert
        operation.Status.Should().Be(BulkOperationStatus.Cancelled);
        operationRepoMock.Verify(r => r.UpdateAsync(
            It.Is<BulkOperation>(op => op.Status == BulkOperationStatus.Cancelled),
            It.IsAny<CancellationToken>()), Times.Once);
        schedulerMock.Verify(s => s.CancelBulkOperationAsync(operation.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelBulkOperationAsync_CompletedOperation_DoesNotUpdate()
    {
        // Arrange
        var operation = new BulkOperation
        {
            OperationName = "test-op",
            Status = BulkOperationStatus.Pending
        };
        operation.MarkRunning();
        operation.MarkCompleted();

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var service = CreateService(operationRepoMock: operationRepoMock);

        // Act
        await service.CancelBulkOperationAsync(operation.Id);

        // Assert
        operation.Status.Should().Be(BulkOperationStatus.Completed);
        operationRepoMock.Verify(r => r.UpdateAsync(It.IsAny<BulkOperation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryBulkOperationsAsync_DelegatesToRepository()
    {
        // Arrange
        var expectedOperations = new List<BulkOperation>
        {
            new() { OperationName = "op-1", Status = BulkOperationStatus.Running },
            new() { OperationName = "op-2", Status = BulkOperationStatus.Running }
        };

        var expectedResult = new PagedResult<BulkOperation>
        {
            Items = expectedOperations,
            TotalCount = 2,
            Page = 1,
            PageSize = 20
        };

        var query = new BulkOperationQuery { Status = BulkOperationStatus.Running };

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.QueryAsync(It.Is<BulkOperationQuery>(q => q.Status == BulkOperationStatus.Running), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var service = CreateService(operationRepoMock: operationRepoMock);

        // Act
        var result = await service.QueryBulkOperationsAsync(query);

        // Assert
        result.Items.Should().BeEquivalentTo(expectedOperations);
        result.TotalCount.Should().Be(2);
        operationRepoMock.Verify(r => r.QueryAsync(It.Is<BulkOperationQuery>(q => q.Status == BulkOperationStatus.Running), It.IsAny<CancellationToken>()), Times.Once);
    }
}

// Fakes must be non-nested and public so Castle.DynamicProxy can access them when mocking generic interfaces
[Trait("Category", "Unit")]
public class ValidationFakeMetadata : IBulkMetadata
{
}

[Trait("Category", "Unit")]
public class ValidationFakeRow : IBulkRow
{
    public string? RowId { get; set; }
}

[BulkOperation("test-op")]
[Trait("Category", "Unit")]
public class ValidationFakeOperation : IBulkRowOperation<ValidationFakeMetadata, ValidationFakeRow>
{
    public Task ValidateMetadataAsync(ValidationFakeMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ValidateRowAsync(ValidationFakeRow row, ValidationFakeMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ProcessRowAsync(ValidationFakeRow row, ValidationFakeMetadata metadata, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
