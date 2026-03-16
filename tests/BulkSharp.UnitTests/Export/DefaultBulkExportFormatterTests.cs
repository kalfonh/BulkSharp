using BulkSharp.Core.Domain.Export;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Export;

namespace BulkSharp.UnitTests.Export;

public class DefaultBulkExportFormatterTests
{
    [Fact]
    public async Task FormatReportAsync_Csv_ShouldIncludeMetadataColumns()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatReportAsync(rows,
            new ExportRequest { Format = ExportFormat.Csv, Mode = ExportMode.Report });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        Assert.Contains("RowNumber", content);
        Assert.Contains("ErrorMessage", content);
    }

    [Fact]
    public async Task FormatDataAsync_Csv_ShouldOnlyIncludeRowDataColumns()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatDataAsync(rows,
            new ExportRequest { Format = ExportFormat.Csv, Mode = ExportMode.Data });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        Assert.DoesNotContain("ErrorMessage", content);
        Assert.Contains("John", content);
    }

    [Fact]
    public async Task FormatReportAsync_Json_ShouldProduceValidJson()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatReportAsync(rows,
            new ExportRequest { Format = ExportFormat.Json, Mode = ExportMode.Report });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        var doc = System.Text.Json.JsonDocument.Parse(content);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task FormatDataAsync_Json_ShouldOnlyIncludeRowData()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRows();

        var stream = await formatter.FormatDataAsync(rows,
            new ExportRequest { Format = ExportFormat.Json, Mode = ExportMode.Data });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        Assert.DoesNotContain("ErrorMessage", content);
        Assert.Contains("John", content);
    }

    [Fact]
    public async Task FormatReportAsync_Csv_WithNullRowData_ShouldStillWork()
    {
        var formatter = new DefaultBulkExportFormatter();
        var rows = CreateTestRowsNoRowData();

        var stream = await formatter.FormatReportAsync(rows,
            new ExportRequest { Format = ExportFormat.Csv, Mode = ExportMode.Report });

        stream.Position = 0;
        var content = await new StreamReader(stream).ReadToEndAsync();
        Assert.Contains("RowNumber", content);
    }

    private static async IAsyncEnumerable<BulkExportRow> CreateTestRows()
    {
        yield return new BulkExportRow
        {
            RowNumber = 1, RowId = "r1", State = RowRecordState.Failed,
            StepName = "Validate", StepIndex = 0,
            ErrorType = BulkErrorType.Validation, ErrorMessage = "Invalid email",
            RetryAttempt = 0, CreatedAt = DateTime.UtcNow,
            RowData = """{"name":"John","email":"bad"}"""
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<BulkExportRow> CreateTestRowsNoRowData()
    {
        yield return new BulkExportRow
        {
            RowNumber = 1, State = RowRecordState.Failed,
            StepName = "Process", StepIndex = 0,
            ErrorType = BulkErrorType.Processing, ErrorMessage = "Error",
            CreatedAt = DateTime.UtcNow
        };
        await Task.CompletedTask;
    }
}
