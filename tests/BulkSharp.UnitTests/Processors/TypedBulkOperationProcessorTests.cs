using System.Text;
using System.Text.Json;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Configuration;
using BulkSharp.Core.Domain.Discovery;
using BulkSharp.Core.Exceptions;
using BulkSharp.Core.Abstractions.DataFormats;
using BulkSharp.Processing.Abstractions;
using BulkSharp.Processing.Processors;
using BulkSharp.Processing.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class TypedBulkOperationProcessorTests
{
    private readonly Mock<IManagedStorageProvider> _storageMock = new();
    private readonly Mock<IDataFormatProcessorFactory<ProcessorTestRow>> _processorFactoryMock = new();
    private readonly Mock<IBulkStepExecutor> _stepExecutorMock = new();
    private readonly Mock<IBulkStepRecordManager> _recordManagerMock = new();
    private readonly Mock<IRowRecordFlushService> _rowRecordFlushServiceMock = new();
    private readonly Mock<IBulkOperationDiscovery> _operationDiscoveryMock = new();
    private readonly Mock<IBulkRowRecordRepository> _rowRecordRepositoryMock = new();
    private readonly Mock<IBulkOperationRepository> _operationRepositoryMock = new();
    private readonly Mock<IBulkOperationEventDispatcher> _eventDispatcherMock = new();
    private readonly BulkSharpOptions _options = new();
    private IOptions<BulkSharpOptions> WrappedOptions => Options.Create(_options);

    private TypedBulkOperationProcessor<TestProcessorOperation, ProcessorTestMetadata, ProcessorTestRow> CreateProcessor()
    {
        _operationDiscoveryMock
            .Setup(d => d.GetOperation("test-processor-op"))
            .Returns(new BulkOperationInfo
            {
                Name = "test-processor-op",
                OperationType = typeof(TestProcessorOperation),
                MetadataType = typeof(ProcessorTestMetadata),
                RowType = typeof(ProcessorTestRow),
                DefaultStepName = "Test Step"
            });

        var validationPipeline = new RowValidationPipeline<ProcessorTestMetadata, ProcessorTestRow>(
            Enumerable.Empty<IBulkRowValidator<ProcessorTestMetadata, ProcessorTestRow>>());

        var strategy = new SequentialRowExecutionStrategy(
            _rowRecordFlushServiceMock.Object, _rowRecordRepositoryMock.Object,
            _operationRepositoryMock.Object, WrappedOptions);

        return new TypedBulkOperationProcessor<TestProcessorOperation, ProcessorTestMetadata, ProcessorTestRow>(
            _storageMock.Object,
            _processorFactoryMock.Object,
            _stepExecutorMock.Object,
            _recordManagerMock.Object,
            Enumerable.Empty<IBulkMetadataValidator<ProcessorTestMetadata>>(),
            validationPipeline,
            Enumerable.Empty<IBulkRowProcessor<ProcessorTestMetadata, ProcessorTestRow>>(),
            strategy,
            _operationDiscoveryMock.Object,
            _rowRecordRepositoryMock.Object,
            _operationRepositoryMock.Object,
            _eventDispatcherMock.Object,
            _rowRecordFlushServiceMock.Object,
            WrappedOptions,
            NullLogger<TypedBulkOperationProcessor<TestProcessorOperation, ProcessorTestMetadata, ProcessorTestRow>>.Instance);
    }

    private void SetupCsvStream(string csv)
    {
        _storageMock
            .Setup(s => s.RetrieveFileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)));

        var mockProcessor = new Mock<IDataFormatProcessor<ProcessorTestRow>>();
        mockProcessor
            .Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream s, CancellationToken ct) => ParseCsvRows(s));

        _processorFactoryMock
            .Setup(f => f.GetProcessor(It.IsAny<string>()))
            .Returns(mockProcessor.Object);
    }

    private static async IAsyncEnumerable<ProcessorTestRow> ParseCsvRows(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(); // skip header
        while (await reader.ReadLineAsync() is { } line)
        {
            var parts = line.Split(',');
            yield return new ProcessorTestRow { Name = parts[0], Email = parts[1] };
        }
    }

    [Fact]
    public async Task ProcessOperationAsync_ValidRows_ProcessesAll()
    {
        SetupCsvStream("Name,Email\nAlice,alice@test.com\nBob,bob@test.com\nCharlie,charlie@test.com");

        var operation = new BulkOperation { Id = Guid.NewGuid(), FileId = Guid.NewGuid(), FileName = "test.csv", OperationName = "test-processor-op" };
        var instance = new TestProcessorOperation();
        var metadata = new ProcessorTestMetadata();

        var processor = CreateProcessor();
        await processor.ProcessOperationAsync(operation, instance, metadata);

        Assert.Equal(3, operation.SuccessfulRows);
        Assert.Equal(0, operation.FailedRows);
        Assert.Equal(3, operation.ProcessedRows);
    }

    [Fact]
    public async Task ProcessOperationAsync_ValidationFailure_RecordsErrorViaBulkRowRecord()
    {
        SetupCsvStream("Name,Email\nAlice,alice@test.com\nBad,invalid");

        var operation = new BulkOperation { Id = Guid.NewGuid(), FileId = Guid.NewGuid(), FileName = "test.csv", OperationName = "test-processor-op" };
        var instance = new TestProcessorOperation();
        var metadata = new ProcessorTestMetadata();

        var capturedRecords = new List<BulkRowRecord>();
        _rowRecordRepositoryMock
            .Setup(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BulkRowRecord>, CancellationToken>((records, _) => capturedRecords.AddRange(records))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor();
        await processor.ProcessOperationAsync(operation, instance, metadata);

        Assert.Equal(1, operation.SuccessfulRows);
        Assert.Equal(1, operation.FailedRows);

        var failedRecords = capturedRecords.Where(r => r.State == RowRecordState.Failed).ToList();
        Assert.Single(failedRecords);
        Assert.Equal(BulkErrorType.Validation, failedRecords[0].ErrorType);
    }

    [Fact]
    public async Task ProcessOperationAsync_PiiNotIncludedByDefault()
    {
        SetupCsvStream("Name,Email\nBad,invalid");

        var operation = new BulkOperation { Id = Guid.NewGuid(), FileId = Guid.NewGuid(), FileName = "test.csv", OperationName = "test-processor-op" };
        var instance = new TestProcessorOperation();
        var metadata = new ProcessorTestMetadata();

        var capturedRecords = new List<BulkRowRecord>();
        _rowRecordRepositoryMock
            .Setup(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BulkRowRecord>, CancellationToken>((records, _) => capturedRecords.AddRange(records))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor();
        await processor.ProcessOperationAsync(operation, instance, metadata);

        // RowData should be null by default (TrackRowData is false)
        Assert.Single(capturedRecords);
        Assert.Null(capturedRecords[0].RowData);
    }

    [Fact]
    public async Task ProcessOperationAsync_RowDataTrackedWhenEnabled()
    {
        SetupCsvStream("Name,Email\nBad,invalid");

        var capturedRecords = new List<BulkRowRecord>();
        _rowRecordRepositoryMock
            .Setup(r => r.CreateBatchAsync(It.IsAny<IEnumerable<BulkRowRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BulkRowRecord>, CancellationToken>((records, _) => capturedRecords.AddRange(records))
            .Returns(Task.CompletedTask);

        // Must set TrackRowData BEFORE CreateProcessor, which also sets up the discovery mock
        _operationDiscoveryMock
            .Setup(d => d.GetOperation("test-processor-op"))
            .Returns(new BulkOperationInfo
            {
                Name = "test-processor-op",
                OperationType = typeof(TestProcessorOperation),
                MetadataType = typeof(ProcessorTestMetadata),
                RowType = typeof(ProcessorTestRow),
                DefaultStepName = "Test Step",
                TrackRowData = true
            });

        var operation = new BulkOperation { Id = Guid.NewGuid(), FileId = Guid.NewGuid(), FileName = "test.csv", OperationName = "test-processor-op" };
        var instance = new TestProcessorOperation();
        var metadata = new ProcessorTestMetadata();

        // CreateProcessor must be called AFTER the discovery mock override so it doesn't reset TrackRowData
        var validationPipeline = new RowValidationPipeline<ProcessorTestMetadata, ProcessorTestRow>(
            Enumerable.Empty<IBulkRowValidator<ProcessorTestMetadata, ProcessorTestRow>>());

        var strategy = new SequentialRowExecutionStrategy(
            _rowRecordFlushServiceMock.Object, _rowRecordRepositoryMock.Object,
            _operationRepositoryMock.Object, WrappedOptions);

        var processor = new TypedBulkOperationProcessor<TestProcessorOperation, ProcessorTestMetadata, ProcessorTestRow>(
            _storageMock.Object,
            _processorFactoryMock.Object,
            _stepExecutorMock.Object,
            _recordManagerMock.Object,
            Enumerable.Empty<IBulkMetadataValidator<ProcessorTestMetadata>>(),
            validationPipeline,
            Enumerable.Empty<IBulkRowProcessor<ProcessorTestMetadata, ProcessorTestRow>>(),
            strategy,
            _operationDiscoveryMock.Object,
            _rowRecordRepositoryMock.Object,
            _operationRepositoryMock.Object,
            _eventDispatcherMock.Object,
            _rowRecordFlushServiceMock.Object,
            WrappedOptions,
            NullLogger<TypedBulkOperationProcessor<TestProcessorOperation, ProcessorTestMetadata, ProcessorTestRow>>.Instance);

        await processor.ProcessOperationAsync(operation, instance, metadata);

        Assert.Single(capturedRecords);
        Assert.NotNull(capturedRecords[0].RowData);
        Assert.Contains("Bad", capturedRecords[0].RowData);
    }

    [Fact]
    public async Task ProcessOperationAsync_StepBased_ExecutesAllSteps()
    {
        SetupCsvStream("Name,Email\nAlice,alice@test.com");

        _operationDiscoveryMock
            .Setup(d => d.GetOperation("test-step-processor-op"))
            .Returns(new BulkOperationInfo
            {
                Name = "test-step-processor-op",
                OperationType = typeof(TestStepBasedOperation),
                MetadataType = typeof(ProcessorTestMetadata),
                RowType = typeof(ProcessorTestRow),
                IsStepBased = true,
                DefaultStepName = "Step1"
            });

        _recordManagerMock
            .Setup(m => m.CreateStepRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid opId, int rowNum, string? rowId, string stepName, int stepIdx, CancellationToken _) =>
                BulkRowRecord.CreateStep(opId, rowNum, rowId, stepName, stepIdx));

        var operation = new BulkOperation { Id = Guid.NewGuid(), FileId = Guid.NewGuid(), FileName = "test.csv", OperationName = "test-step-processor-op" };
        var instance = new TestStepBasedOperation();
        var metadata = new ProcessorTestMetadata();

        var stepValidationPipeline = new RowValidationPipeline<ProcessorTestMetadata, ProcessorTestRow>(
            Enumerable.Empty<IBulkRowValidator<ProcessorTestMetadata, ProcessorTestRow>>());

        var strategy = new SequentialRowExecutionStrategy(
            _rowRecordFlushServiceMock.Object, _rowRecordRepositoryMock.Object,
            _operationRepositoryMock.Object, WrappedOptions);

        var processor = new TypedBulkOperationProcessor<TestStepBasedOperation, ProcessorTestMetadata, ProcessorTestRow>(
            _storageMock.Object,
            _processorFactoryMock.Object,
            _stepExecutorMock.Object,
            _recordManagerMock.Object,
            Enumerable.Empty<IBulkMetadataValidator<ProcessorTestMetadata>>(),
            stepValidationPipeline,
            Enumerable.Empty<IBulkRowProcessor<ProcessorTestMetadata, ProcessorTestRow>>(),
            strategy,
            _operationDiscoveryMock.Object,
            _rowRecordRepositoryMock.Object,
            _operationRepositoryMock.Object,
            _eventDispatcherMock.Object,
            _rowRecordFlushServiceMock.Object,
            WrappedOptions,
            NullLogger<TypedBulkOperationProcessor<TestStepBasedOperation, ProcessorTestMetadata, ProcessorTestRow>>.Instance);

        await processor.ProcessOperationAsync(operation, instance, metadata);

        Assert.Equal(1, operation.SuccessfulRows);
        _stepExecutorMock.Verify(
            s => s.ExecuteStepAsync(It.IsAny<IBulkStep<ProcessorTestMetadata, ProcessorTestRow>>(),
                It.IsAny<ProcessorTestRow>(), It.IsAny<ProcessorTestMetadata>(),
                It.IsAny<BulkRowRecord>(), It.IsAny<IBulkStepRecordManager>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}

[Trait("Category", "Unit")]
public class ProcessorTestMetadata : IBulkMetadata { }

[Trait("Category", "Unit")]
public class ProcessorTestRow : IBulkRow
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? RowId { get; set; }
}

[BulkOperation("test-processor-op")]
[Trait("Category", "Unit")]
public class TestProcessorOperation : IBulkRowOperation<ProcessorTestMetadata, ProcessorTestRow>
{
    public Task ValidateMetadataAsync(ProcessorTestMetadata metadata, CancellationToken ct = default) => Task.CompletedTask;

    public Task ValidateRowAsync(ProcessorTestRow row, ProcessorTestMetadata metadata, CancellationToken ct = default)
    {
        if (!row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email: {row.Email}");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(ProcessorTestRow row, ProcessorTestMetadata metadata, CancellationToken ct = default)
        => Task.CompletedTask;
}

[BulkOperation("test-step-processor-op")]
[Trait("Category", "Unit")]
public class TestStepBasedOperation : IBulkPipelineOperation<ProcessorTestMetadata, ProcessorTestRow>
{
    public Task ValidateMetadataAsync(ProcessorTestMetadata metadata, CancellationToken ct = default) => Task.CompletedTask;
    public Task ValidateRowAsync(ProcessorTestRow row, ProcessorTestMetadata metadata, CancellationToken ct = default)
        => Task.CompletedTask;

    public IEnumerable<IBulkStep<ProcessorTestMetadata, ProcessorTestRow>> GetSteps()
    {
        yield return new TestStep("Step1");
        yield return new TestStep("Step2");
    }

    private class TestStep(string name) : IBulkStep<ProcessorTestMetadata, ProcessorTestRow>
    {
        public string Name => name;
        public int MaxRetries => 1;
        public Task ExecuteAsync(ProcessorTestRow row, ProcessorTestMetadata metadata, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
