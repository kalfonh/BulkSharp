using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Steps;
using FluentAssertions;
using Xunit;

namespace BulkSharp.UnitTests.Steps;

[Trait("Category", "Unit")]
public class DelegateStepTests
{
    [Fact]
    public void Create_ReturnsStepWithNameAndRetries()
    {
        var step = Step.Create<StepTestMetadata, StepTestRow>(
            "Import Users", (_, _, _) => Task.CompletedTask, maxRetries: 3);

        step.Name.Should().Be("Import Users");
        step.MaxRetries.Should().Be(3);
    }

    [Fact]
    public async Task Create_DelegateIsInvoked()
    {
        var invoked = false;
        var step = Step.Create<StepTestMetadata, StepTestRow>(
            "Test Step",
            (row, meta, ct) =>
            {
                invoked = true;
                return Task.CompletedTask;
            });

        await step.ExecuteAsync(new StepTestRow(), new StepTestMetadata(), CancellationToken.None);

        invoked.Should().BeTrue();
    }

    [Fact]
    public void From_ReadsAttributeFromMethod()
    {
        var step = Step.From<StepTestMetadata, StepTestRow>(AnnotatedStep);

        step.Name.Should().Be("Annotated Step");
        step.MaxRetries.Should().Be(2);
    }

    [Fact]
    public void From_ThrowsWhenAttributeMissing()
    {
        var act = () => Step.From<StepTestMetadata, StepTestRow>(UnannotatedStep);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing the [BulkStep] attribute*");
    }

    [BulkStep("Annotated Step", MaxRetries = 2)]
    private static Task AnnotatedStep(StepTestRow row, StepTestMetadata meta, CancellationToken ct) =>
        Task.CompletedTask;

    private static Task UnannotatedStep(StepTestRow row, StepTestMetadata meta, CancellationToken ct) =>
        Task.CompletedTask;
}

public class StepTestMetadata : IBulkMetadata { }

public class StepTestRow : IBulkRow
{
    public string? GetId() => null;
    public string? RowId { get; set; }
}
