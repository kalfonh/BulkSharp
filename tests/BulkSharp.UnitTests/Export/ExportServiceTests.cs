using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Domain.Discovery;
using BulkSharp.Core.Domain.Export;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Export;
using BulkSharp.Processing.Services;
using BulkSharp.Processing.Storage.InMemory;
using Moq;

namespace BulkSharp.UnitTests.Export;

[Trait("Category", "Unit")]
public class ExportServiceTests
{
    private readonly InMemoryBulkOperationRepository _operationRepo = new();
    private readonly InMemoryBulkRowRecordRepository _rowRecordRepo = new();
    private readonly Mock<IBulkOperationDiscovery> _discoveryMock = new();
    private readonly DefaultBulkExportFormatter _formatter = new();
    private readonly BulkExportService _service;

    public ExportServiceTests()
    {
        _service = new BulkExportService(_operationRepo, _rowRecordRepo, _discoveryMock.Object, _formatter);
    }

    [Fact]
    public async Task ExportReportMode_CsvContainsMetadataColumns()
    {
        var opId = Guid.NewGuid();
        await SeedOperation(opId, "TestOp");
        await SeedRowRecord(opId, rowNumber: 1, stepIndex: 0, stepName: "Process",
            state: RowRecordState.Completed, rowData: """{"name":"Alice"}""");

        SetupDiscovery("TestOp", trackRowData: true);

        var result = await _service.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Csv
        });

        var content = await ReadStream(result.Stream);
        Assert.Contains("RowNumber", content);
        Assert.Contains("ErrorMessage", content);
        Assert.Contains("StepName", content);
        Assert.Equal(1, result.RowCount);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Contains("report", result.FileName);
    }

    [Fact]
    public async Task ExportDataMode_WithoutTrackRowData_Throws()
    {
        var opId = Guid.NewGuid();
        await SeedOperation(opId, "TestOp");

        SetupDiscovery("TestOp", trackRowData: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExportAsync(opId, new ExportRequest { Mode = ExportMode.Data }));
    }

    [Fact]
    public async Task ExportDataMode_WithTrackRowData_ReturnsRowDataOnly()
    {
        var opId = Guid.NewGuid();
        await SeedOperation(opId, "TestOp");

        // Validation record holds the row data (StepIndex=-1)
        await SeedRowRecord(opId, rowNumber: 1, stepIndex: -1, stepName: "validation",
            state: RowRecordState.Completed, rowData: """{"name":"Bob","email":"bob@test.com"}""");
        // Step record has no row data
        await SeedRowRecord(opId, rowNumber: 1, stepIndex: 0, stepName: "Process",
            state: RowRecordState.Completed, rowData: null);

        SetupDiscovery("TestOp", trackRowData: true);

        var result = await _service.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Data,
            Format = ExportFormat.Csv
        });

        var content = await ReadStream(result.Stream);
        Assert.Contains("Bob", content);
        Assert.Contains("bob@test.com", content);
        Assert.DoesNotContain("ErrorMessage", content);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Contains("data", result.FileName);
    }

    [Fact]
    public async Task ExportWithStateFilter_OnlyReturnsMatchingRows()
    {
        var opId = Guid.NewGuid();
        await SeedOperation(opId, "TestOp");

        await SeedRowRecord(opId, rowNumber: 1, stepIndex: 0, stepName: "Process",
            state: RowRecordState.Completed, rowData: """{"name":"Success"}""");
        await SeedRowRecord(opId, rowNumber: 2, stepIndex: 0, stepName: "Process",
            state: RowRecordState.Failed, errorMessage: "Bad data",
            errorType: BulkErrorType.Processing, rowData: """{"name":"Failed"}""");

        SetupDiscovery("TestOp", trackRowData: true);

        var result = await _service.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Csv,
            State = RowRecordState.Failed
        });

        var content = await ReadStream(result.Stream);
        Assert.Contains("Failed", content);
        Assert.DoesNotContain("Success", content);
        Assert.Equal(1, result.RowCount);
    }

    [Fact]
    public async Task ExportNoMatchingRows_ReturnsEmptyStreamWithZeroCount()
    {
        var opId = Guid.NewGuid();
        await SeedOperation(opId, "TestOp");

        SetupDiscovery("TestOp", trackRowData: false);

        var result = await _service.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Json
        });

        Assert.Equal(0, result.RowCount);
        Assert.Equal("application/json", result.ContentType);
    }

    [Fact]
    public async Task LatestStepPerRow_ExportsOnlyHighestStepIndex()
    {
        var opId = Guid.NewGuid();
        await SeedOperation(opId, "TestOp");

        // Row 1 has two step records; only the latest (StepIndex=1) should be exported
        await SeedRowRecord(opId, rowNumber: 1, stepIndex: 0, stepName: "Step1",
            state: RowRecordState.Completed, rowData: """{"name":"OldStep"}""");
        await SeedRowRecord(opId, rowNumber: 1, stepIndex: 1, stepName: "Step2",
            state: RowRecordState.Failed, errorMessage: "Step2 failed",
            errorType: BulkErrorType.Processing, rowData: """{"name":"LatestStep"}""");

        SetupDiscovery("TestOp", trackRowData: true);

        var result = await _service.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Csv
        });

        var content = await ReadStream(result.Stream);
        Assert.Contains("Step2", content);
        Assert.Contains("Step2 failed", content);
        Assert.Equal(1, result.RowCount);
    }

    private async Task SeedOperation(Guid id, string name)
    {
        await _operationRepo.CreateAsync(new BulkOperation
        {
            Id = id,
            OperationName = name,
            Status = BulkOperationStatus.Completed
        });
    }

    private async Task SeedRowRecord(
        Guid operationId,
        int rowNumber,
        int stepIndex,
        string stepName,
        RowRecordState state,
        string? rowData = null,
        string? errorMessage = null,
        BulkErrorType? errorType = null)
    {
        var record = new BulkRowRecord
        {
            BulkOperationId = operationId,
            RowNumber = rowNumber,
            StepIndex = stepIndex,
            StepName = stepName,
            State = state,
            RowData = rowData,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
            CreatedAt = DateTime.UtcNow
        };
        await _rowRecordRepo.CreateAsync(record);
    }

    private void SetupDiscovery(string name, bool trackRowData)
    {
        _discoveryMock.Setup(d => d.GetOperation(name)).Returns(new BulkOperationInfo
        {
            Name = name,
            RowType = typeof(object),
            TrackRowData = trackRowData
        });
    }

    private static async Task<string> ReadStream(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
