using System.Diagnostics;
using BulkSharp;
using BulkSharp.Core.Configuration;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class ParallelRowProcessingTests
{
    [Fact]
    public async Task ProcessOperation_WithParallelRowConcurrency_ExecutesConcurrently()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBulkSharpInMemory();
        services.Configure<BulkSharpOptions>(o => o.MaxRowConcurrency = 3);
        services.AddScoped<TestParallelOperation>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var operationService = provider.GetRequiredService<IBulkOperationService>();
        var processor = provider.GetRequiredService<IBulkOperationProcessor>();

        var csvContent = "Name\nAlice\nBob\nCharlie";
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new TestParallelMetadata();

        // Act
        var operationId = await operationService.CreateBulkOperationAsync(
            "test-parallel", csvStream, "parallel.csv", metadata, "test-runner");

        var stopwatch = Stopwatch.StartNew();
        await processor.ProcessOperationAsync(operationId);
        stopwatch.Stop();

        // Assert
        var operation = await operationService.GetBulkOperationAsync(operationId);
        Assert.NotNull(operation);
        Assert.Equal(BulkOperationStatus.Completed, operation.Status);
        Assert.Equal(3, operation.TotalRows);
        Assert.Equal(3, operation.ProcessedRows);
        Assert.Equal(3, operation.SuccessfulRows);
        Assert.Equal(0, operation.FailedRows);

        // 3 rows x 2s each: parallel should be ~2s, sequential would be ~6s.
        // Use 5s threshold to avoid flaky tests while still proving parallelism.
        Assert.True(stopwatch.Elapsed.TotalSeconds < 5,
            $"Expected parallel execution under 5s, but took {stopwatch.Elapsed.TotalSeconds:F1}s. " +
            "Rows may be executing sequentially instead of in parallel.");
    }
}

[BulkOperation("test-parallel")]
public class TestParallelOperation : IBulkPipelineOperation<TestParallelMetadata, TestParallelRow>
{
    public Task ValidateMetadataAsync(TestParallelMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ValidateRowAsync(TestParallelRow row, TestParallelMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public IEnumerable<IBulkStep<TestParallelMetadata, TestParallelRow>> GetSteps()
    {
        yield return new SlowAsyncStep();
    }

    private sealed class SlowAsyncStep : IBulkStep<TestParallelMetadata, TestParallelRow>
    {
        public string Name => "SlowAsync";
        public int MaxRetries => 1;

        public async Task ExecuteAsync(TestParallelRow row, TestParallelMetadata metadata, CancellationToken cancellationToken = default)
        {
            await Task.Delay(2000, cancellationToken);
        }
    }
}

public class TestParallelMetadata : IBulkMetadata
{
}

[CsvSchema("1.0")]
public class TestParallelRow : IBulkRow
{
    [CsvColumn("Name")]
    public string Name { get; set; } = string.Empty;
    public string? RowId { get; set; }
}
