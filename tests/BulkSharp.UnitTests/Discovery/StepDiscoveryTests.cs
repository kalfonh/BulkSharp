using BulkSharp.Core.Domain.Discovery;
using BulkSharp.Processing.Processors;
using BulkSharp.Processing.Services;

namespace BulkSharp.UnitTests.Discovery;

[Trait("Category", "Unit")]
public class StepDiscoveryTests
{
    [Fact]
    public void ScanAssemblies_ReadsDefaultStepNameFromAttribute()
    {
        var results = BulkOperationDiscoveryService
            .ScanAssemblies(new[] { typeof(StepDiscoveryTests).Assembly })
            .ToList();

        var info = results.SingleOrDefault(r => r.Name == "step-attr-test");

        info.Should().NotBeNull();
        info!.DefaultStepName.Should().Be("Process Payment");
    }

    [Fact]
    public void ScanAssemblies_FallsBackToOperationName_WhenNoAttribute()
    {
        var results = BulkOperationDiscoveryService
            .ScanAssemblies(new[] { typeof(StepDiscoveryTests).Assembly })
            .ToList();

        var info = results.SingleOrDefault(r => r.Name == "no-step-attr-test");

        info.Should().NotBeNull();
        info!.DefaultStepName.Should().Be("no-step-attr-test");
    }

    [Fact]
    public void DiscoverStepsFromAttributes_FindsAnnotatedMethods_InOrder()
    {
        var instance = new StepBasedOpWithAttributes();
        var steps = TypedBulkOperationProcessor<StepBasedOpWithAttributes, DiscoveryMeta, DiscoveryRow>
            .DiscoverStepsFromAttributes<DiscoveryMeta, DiscoveryRow>(instance.GetType(), instance);

        steps.Should().HaveCount(2);
        steps[0].Name.Should().Be("Step A");
        steps[0].MaxRetries.Should().Be(3);
        steps[1].Name.Should().Be("Step B");
        steps[1].MaxRetries.Should().Be(2);
    }

    [Fact]
    public void DiscoverStepsFromAttributes_ReturnsEmpty_WhenNoAnnotatedMethods()
    {
        var instance = new StepBasedOpWithoutAttributes();
        var steps = TypedBulkOperationProcessor<StepBasedOpWithoutAttributes, DiscoveryMeta, DiscoveryRow>
            .DiscoverStepsFromAttributes<DiscoveryMeta, DiscoveryRow>(instance.GetType(), instance);

        steps.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverStepsFromAttributes_CreatedSteps_AreFunctional()
    {
        var instance = new StepBasedOpWithAttributes();
        var steps = TypedBulkOperationProcessor<StepBasedOpWithAttributes, DiscoveryMeta, DiscoveryRow>
            .DiscoverStepsFromAttributes<DiscoveryMeta, DiscoveryRow>(instance.GetType(), instance);

        var row = new DiscoveryRow();
        var meta = new DiscoveryMeta();

        // Should not throw
        await steps[0].ExecuteAsync(row, meta);
        await steps[1].ExecuteAsync(row, meta);
    }

    // --- Nested fixture types for auto-discovery tests ---

    private sealed class DiscoveryMeta : IBulkMetadata { }

    private sealed class DiscoveryRow : IBulkRow
    {
        public string? RowId { get; set; }
    }

    [BulkOperation("discovery-test-step-based")]
    private sealed class StepBasedOpWithAttributes : IBulkPipelineOperation<DiscoveryMeta, DiscoveryRow>
    {
        public Task ValidateMetadataAsync(DiscoveryMeta metadata, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ValidateRowAsync(DiscoveryRow row, DiscoveryMeta metadata, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        [BulkStep("Step B", Order = 1, MaxRetries = 2)]
        public Task StepBAsync(DiscoveryRow row, DiscoveryMeta meta, CancellationToken ct) => Task.CompletedTask;

        [BulkStep("Step A", Order = 0, MaxRetries = 3)]
        public Task StepAAsync(DiscoveryRow row, DiscoveryMeta meta, CancellationToken ct) => Task.CompletedTask;
    }

    [BulkOperation("discovery-test-no-attrs")]
    private sealed class StepBasedOpWithoutAttributes : IBulkPipelineOperation<DiscoveryMeta, DiscoveryRow>
    {
        public Task ValidateMetadataAsync(DiscoveryMeta metadata, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ValidateRowAsync(DiscoveryRow row, DiscoveryMeta metadata, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SomeMethod(DiscoveryRow row, DiscoveryMeta meta, CancellationToken ct) => Task.CompletedTask;
    }
}

// --- Test fixture types ---

file sealed class StepAttrMetadata : IBulkMetadata
{
}

file sealed class StepAttrRow : IBulkRow
{
    public string? RowId { get; set; }
}

[BulkOperation("step-attr-test")]
file sealed class WithStepAttrOperation : IBulkRowOperation<StepAttrMetadata, StepAttrRow>
{
    public Task ValidateMetadataAsync(StepAttrMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ValidateRowAsync(StepAttrRow row, StepAttrMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    [BulkStep("Process Payment")]
    public Task ProcessRowAsync(StepAttrRow row, StepAttrMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

[BulkOperation("no-step-attr-test")]
file sealed class WithoutStepAttrOperation : IBulkRowOperation<StepAttrMetadata, StepAttrRow>
{
    public Task ValidateMetadataAsync(StepAttrMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ValidateRowAsync(StepAttrRow row, StepAttrMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ProcessRowAsync(StepAttrRow row, StepAttrMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
