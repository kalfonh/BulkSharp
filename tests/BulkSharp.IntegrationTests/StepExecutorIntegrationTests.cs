using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class StepExecutorIntegrationTests
{
    [Fact]
    public async Task ExecuteStepAsync_WithValidationStep_ExecutesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBulkSharp(builder =>
        {
            builder.UseFileStorage(fs => fs.UseInMemory())
                   .UseMetadataStorage(ms => ms.UseInMemory())
                   .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestOperation>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IBulkStepExecutor>();
        var operation = provider.GetRequiredService<TestOperation>();

        var metadata = new TestMetadata { RequestedBy = "test" };
        var row = new TestRow { Name = "Test" };

        var step = new ValidationStepAdapter(operation);
        await executor.ExecuteStepAsync(step, row, metadata);
        Assert.True(step.Executed);
    }

    [Fact]
    public async Task ExecuteStepAsync_WithProcessingStep_ExecutesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBulkSharp(builder =>
        {
            builder.UseFileStorage(fs => fs.UseInMemory())
                   .UseMetadataStorage(ms => ms.UseInMemory())
                   .UseScheduler(s => s.UseImmediate());
        });
        services.AddScoped<TestOperation>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IBulkStepExecutor>();
        var operation = provider.GetRequiredService<TestOperation>();

        var metadata = new TestMetadata { RequestedBy = "test" };
        var row = new TestRow { Name = "Test" };

        var step = new ProcessingStepAdapter(operation);
        await executor.ExecuteStepAsync(step, row, metadata);
        Assert.True(step.Executed);
    }

    private class TestMetadata : IBulkMetadata
    {
        public string RequestedBy { get; set; } = string.Empty;
    }

    private class TestRow : IBulkRow
    {
        public string Name { get; set; } = string.Empty;
        public string? RowId { get; set; }
    }

    private class TestOperation : IBulkRowOperation<TestMetadata, TestRow>
    {
        public Task ValidateMetadataAsync(TestMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(metadata.RequestedBy))
                throw new BulkValidationException("RequestedBy is required");
            return Task.CompletedTask;
        }

        public Task ValidateRowAsync(TestRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(row.Name))
                throw new BulkValidationException("Name is required");
            return Task.CompletedTask;
        }

        public Task ProcessRowAsync(TestRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class ValidationStepAdapter : IBulkStep<TestMetadata, TestRow>
    {
        private readonly TestOperation _operation;
        public ValidationStepAdapter(TestOperation operation) => _operation = operation;
        public string Name => "Validation";
        public int MaxRetries => 1;
        public bool Executed { get; private set; }
        public async Task ExecuteAsync(TestRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
        {
            await _operation.ValidateMetadataAsync(metadata, cancellationToken);
            await _operation.ValidateRowAsync(row, metadata, cancellationToken);
            Executed = true;
        }
    }

    private class ProcessingStepAdapter : IBulkStep<TestMetadata, TestRow>
    {
        private readonly TestOperation _operation;
        public ProcessingStepAdapter(TestOperation operation) => _operation = operation;
        public string Name => "Processing";
        public int MaxRetries => 3;
        public bool Executed { get; private set; }
        public async Task ExecuteAsync(TestRow row, TestMetadata metadata, CancellationToken cancellationToken = default)
        {
            await _operation.ProcessRowAsync(row, metadata, cancellationToken);
            Executed = true;
        }
    }
}
