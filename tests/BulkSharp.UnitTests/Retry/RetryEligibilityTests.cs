using BulkSharp.Core.Configuration;
using BulkSharp.Core.Domain.Discovery;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Domain.Retry;
using BulkSharp.Processing.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BulkSharp.UnitTests.Retry;

[Trait("Category", "Unit")]
public class RetryEligibilityTests
{
    private static BulkRetryService CreateService(
        Mock<IBulkOperationRepository>? operationRepoMock = null,
        Mock<IBulkRowRecordRepository>? rowRecordRepoMock = null,
        Mock<IBulkRowRetryHistoryRepository>? retryHistoryRepoMock = null,
        Mock<IBulkOperationDiscovery>? discoveryMock = null,
        Mock<IBulkScheduler>? schedulerMock = null,
        BulkSharpOptions? options = null)
    {
        return new BulkRetryService(
            (operationRepoMock ?? new Mock<IBulkOperationRepository>()).Object,
            (rowRecordRepoMock ?? new Mock<IBulkRowRecordRepository>()).Object,
            (retryHistoryRepoMock ?? new Mock<IBulkRowRetryHistoryRepository>()).Object,
            (discoveryMock ?? new Mock<IBulkOperationDiscovery>()).Object,
            (schedulerMock ?? new Mock<IBulkScheduler>()).Object,
            Options.Create(options ?? new BulkSharpOptions()),
            NullLogger<BulkRetryService>.Instance);
    }

    private static BulkOperationInfo CreateRetryableOpInfo(
        string name = "test-op",
        bool isRetryable = true,
        bool trackRowData = true,
        Dictionary<string, bool>? stepRetryability = null) => new()
    {
        Name = name,
        OperationType = typeof(object),
        MetadataType = typeof(object),
        RowType = typeof(object),
        IsRetryable = isRetryable,
        TrackRowData = trackRowData,
        StepRetryability = stepRetryability ?? new Dictionary<string, bool>()
    };

    private static BulkOperation CreateCompletedWithErrorsOperation(string operationName = "test-op", int retryCount = 0)
    {
        var op = new BulkOperation
        {
            OperationName = operationName,
            Status = BulkOperationStatus.Pending,
            RetryCount = retryCount
        };
        op.MarkRunning();
        op.RecordRowResult(false);
        op.MarkCompleted();
        return op;
    }

    private static Mock<IBulkRowRecordRepository> CreateRowRepoWithFailedRows(Guid operationId, int count)
    {
        var mock = new Mock<IBulkRowRecordRepository>();
        var rows = Enumerable.Range(1, count).Select(i => new BulkRowRecord
        {
            BulkOperationId = operationId,
            RowNumber = i,
            StepName = "process",
            StepIndex = 0,
            State = RowRecordState.Failed,
            ErrorType = BulkErrorType.Processing,
            ErrorMessage = "Test error"
        }).ToList();

        mock.Setup(r => r.QueryAsync(It.IsAny<BulkRowRecordQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BulkRowRecord>
            {
                Items = rows,
                TotalCount = count,
                Page = 1,
                PageSize = 1000
            });

        return mock;
    }

    [Fact]
    public async Task CanRetry_OperationNotFound_ReturnsIneligible()
    {
        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulkOperation?)null);

        var service = CreateService(operationRepoMock: operationRepoMock);

        var result = await service.CanRetryAsync(Guid.NewGuid());

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("not found");
    }

    [Fact]
    public async Task CanRetry_OperationStillRunning_ReturnsIneligible()
    {
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

        var service = CreateService(operationRepoMock: operationRepoMock);

        var result = await service.CanRetryAsync(operation.Id);

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("Running");
    }

    [Fact]
    public async Task CanRetry_OperationNotRetryable_ReturnsIneligible()
    {
        var operation = CreateCompletedWithErrorsOperation();

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock
            .Setup(d => d.GetOperation("test-op"))
            .Returns(CreateRetryableOpInfo(isRetryable: false));

        var service = CreateService(operationRepoMock: operationRepoMock, discoveryMock: discoveryMock);

        var result = await service.CanRetryAsync(operation.Id);

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("not retryable");
    }

    [Fact]
    public async Task CanRetry_TrackRowDataDisabled_ReturnsIneligible()
    {
        var operation = CreateCompletedWithErrorsOperation();

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock
            .Setup(d => d.GetOperation("test-op"))
            .Returns(CreateRetryableOpInfo(trackRowData: false));

        var service = CreateService(operationRepoMock: operationRepoMock, discoveryMock: discoveryMock);

        var result = await service.CanRetryAsync(operation.Id);

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("TrackRowData");
    }

    [Fact]
    public async Task CanRetry_MaxRetryAttemptsExceeded_ReturnsIneligible()
    {
        var operation = CreateCompletedWithErrorsOperation(retryCount: 3);

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock
            .Setup(d => d.GetOperation("test-op"))
            .Returns(CreateRetryableOpInfo());

        var service = CreateService(
            operationRepoMock: operationRepoMock,
            discoveryMock: discoveryMock,
            options: new BulkSharpOptions { MaxRetryAttempts = 3 });

        var result = await service.CanRetryAsync(operation.Id);

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("Maximum retry attempts");
    }

    [Fact]
    public async Task CanRetry_NoFailedRows_ReturnsIneligible()
    {
        var operation = CreateCompletedWithErrorsOperation();

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock
            .Setup(d => d.GetOperation("test-op"))
            .Returns(CreateRetryableOpInfo());

        var rowRecordRepoMock = new Mock<IBulkRowRecordRepository>();
        rowRecordRepoMock
            .Setup(r => r.QueryAsync(It.IsAny<BulkRowRecordQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BulkRowRecord>
            {
                Items = [],
                TotalCount = 0,
                Page = 1,
                PageSize = 1000
            });

        var service = CreateService(
            operationRepoMock: operationRepoMock,
            discoveryMock: discoveryMock,
            rowRecordRepoMock: rowRecordRepoMock);

        var result = await service.CanRetryAsync(operation.Id);

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("No retryable failed rows");
    }

    [Fact]
    public async Task CanRetry_ValidOperation_ReturnsEligible()
    {
        var operation = CreateCompletedWithErrorsOperation();

        var operationRepoMock = new Mock<IBulkOperationRepository>();
        operationRepoMock
            .Setup(r => r.GetByIdAsync(operation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var discoveryMock = new Mock<IBulkOperationDiscovery>();
        discoveryMock
            .Setup(d => d.GetOperation("test-op"))
            .Returns(CreateRetryableOpInfo());

        var rowRecordRepoMock = CreateRowRepoWithFailedRows(operation.Id, 3);

        var service = CreateService(
            operationRepoMock: operationRepoMock,
            discoveryMock: discoveryMock,
            rowRecordRepoMock: rowRecordRepoMock);

        var result = await service.CanRetryAsync(operation.Id);

        result.IsEligible.Should().BeTrue();
        result.Reason.Should().BeNull();
    }
}
