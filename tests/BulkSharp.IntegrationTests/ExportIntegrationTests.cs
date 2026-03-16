using System.Text.Json;
using BulkSharp.Core.Domain.Export;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class ExportIntegrationTests
{
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<ExportTestTrackedOperation>();
        services.AddScoped<ExportTestUntrackedOperation>();
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    private static async Task<string> ReadStreamAsString(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task ExportReportModeCsv_ContainsMetadataColumnsAndRowData()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var exportService = provider.GetRequiredService<IBulkExportService>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new ExportTestMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("export-tracked-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        Assert.Equal(BulkOperationStatus.CompletedWithErrors, operation!.Status);

        var result = await exportService.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Csv
        });

        var content = await ReadStreamAsString(result.Stream);
        Assert.True(result.RowCount > 0);

        // Verify metadata columns present in header
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0];
        Assert.Contains("RowNumber", header);
        Assert.Contains("State", header);
        Assert.Contains("ErrorMessage", header);

        // Verify row data columns present (from tracked row data)
        Assert.Contains("Name", header);
        Assert.Contains("Email", header);
        Assert.Contains("Age", header);
    }

    [Fact]
    public async Task ExportDataModeCsv_ContainsOnlyRowSchemaColumns()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var exportService = provider.GetRequiredService<IBulkExportService>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new ExportTestMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("export-tracked-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var result = await exportService.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Data,
            Format = ExportFormat.Csv,
            State = RowRecordState.Failed
        });

        var content = await ReadStreamAsString(result.Stream);
        Assert.True(result.RowCount > 0);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0];

        // Data mode should have row schema columns only
        Assert.Contains("Name", header);
        Assert.Contains("Email", header);
        Assert.Contains("Age", header);

        // Should NOT have metadata columns
        Assert.DoesNotContain("RowNumber", header);
        Assert.DoesNotContain("ErrorMessage", header);
        Assert.DoesNotContain("StepIndex", header);
    }

    [Fact]
    public async Task ExportWithStateFilter_OnlyFailedRowsAppear()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var exportService = provider.GetRequiredService<IBulkExportService>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new ExportTestMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("export-tracked-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var operation = await operationService.GetBulkOperationAsync(opId);
        var failedCount = operation!.FailedRows;
        Assert.True(failedCount > 0);

        var result = await exportService.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Csv,
            State = RowRecordState.Failed
        });

        var content = await ReadStreamAsString(result.Stream);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + failed rows only
        Assert.Equal(failedCount + 1, lines.Length);
        Assert.Equal(failedCount, result.RowCount);

        // Verify all data rows show Failed state
        for (var i = 1; i < lines.Length; i++)
        {
            Assert.Contains("Failed", lines[i]);
        }
    }

    [Fact]
    public async Task ExportReportMode_WithTrackRowDataFalse_WorksWithoutRowDataColumns()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var exportService = provider.GetRequiredService<IBulkExportService>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new ExportTestMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("export-untracked-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var result = await exportService.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Csv
        });

        var content = await ReadStreamAsString(result.Stream);
        Assert.True(result.RowCount > 0);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0];

        // Metadata columns should still be present
        Assert.Contains("RowNumber", header);
        Assert.Contains("State", header);
        Assert.Contains("ErrorMessage", header);

        // Row data columns should NOT be present (TrackRowData = false)
        Assert.DoesNotContain("Email", header);
        Assert.DoesNotContain("Age", header);
    }

    [Fact]
    public async Task ExportJsonFormat_ReturnsValidJsonArray()
    {
        var provider = BuildProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();
        var exportService = provider.GetRequiredService<IBulkExportService>();

        var csv = "Name,Email,Age\nalice,alice@test.com,30\nbob,bob@test.com,25\ncharlie,charlie@test.com,35";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var metadata = new ExportTestMetadata { RequestedBy = "admin" };

        var opId = await operationService.CreateBulkOperationAsync("export-tracked-op", stream, "test.csv", metadata, "admin");
        await processor.ProcessOperationAsync(opId);

        var result = await exportService.ExportAsync(opId, new ExportRequest
        {
            Mode = ExportMode.Report,
            Format = ExportFormat.Json
        });

        var content = await ReadStreamAsString(result.Stream);
        Assert.True(result.RowCount > 0);
        Assert.Equal("application/json", result.ContentType);

        // Verify valid JSON array
        var doc = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(result.RowCount, doc.RootElement.GetArrayLength());

        // Verify each element has expected metadata properties
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            Assert.True(element.TryGetProperty("RowNumber", out _));
            Assert.True(element.TryGetProperty("State", out _));
        }
    }
}

// --- Test metadata ---

public class ExportTestMetadata : IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
}

[CsvSchema("1.0")]
public class ExportTestCsvRow : IBulkRow
{
    [CsvColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [CsvColumn("Email")]
    public string Email { get; set; } = string.Empty;

    [CsvColumn("Age")]
    public int Age { get; set; }

    public string? RowId { get; set; }
}

// --- Operation with TrackRowData = true, bob (row 2) always fails ---

[BulkOperation("export-tracked-op", TrackRowData = true)]
public class ExportTestTrackedOperation : IBulkRowOperation<ExportTestMetadata, ExportTestCsvRow>
{
    public Task ValidateMetadataAsync(ExportTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(ExportTestCsvRow row, ExportTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(ExportTestCsvRow row, ExportTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        // bob always fails during processing
        if (row.Name == "bob")
            throw new InvalidOperationException($"Processing failed for {row.Name}");

        return Task.CompletedTask;
    }
}

// --- Operation with TrackRowData = false (default) ---

[BulkOperation("export-untracked-op")]
public class ExportTestUntrackedOperation : IBulkRowOperation<ExportTestMetadata, ExportTestCsvRow>
{
    public Task ValidateMetadataAsync(ExportTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(ExportTestCsvRow row, ExportTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
            throw new BulkValidationException($"Invalid email for {row.Name}.");
        return Task.CompletedTask;
    }

    public Task ProcessRowAsync(ExportTestCsvRow row, ExportTestMetadata metadata, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
