using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Exceptions;
using BulkSharp.Processing.Abstractions;
using BulkSharp.Processing.Processors;

namespace BulkSharp.UnitTests.Processors;

[Trait("Category", "Unit")]
public class RowValidationPipelineTests
{
    private const int RowNumber = 1;

    private RowValidationPipeline<ValidationPipelineMetadata, ValidationPipelineRow> CreatePipeline(
        IEnumerable<IBulkRowValidator<ValidationPipelineMetadata, ValidationPipelineRow>>? validators = null)
    {
        return new RowValidationPipeline<ValidationPipelineMetadata, ValidationPipelineRow>(
            validators ?? []);
    }

    [Fact]
    public async Task ValidateRowAsync_NoValidatorsAndOperationPasses_ReturnsNull()
    {
        var pipeline = CreatePipeline();
        var operation = new Mock<IBulkOperationBase<ValidationPipelineMetadata, ValidationPipelineRow>>();

        var result = await pipeline.ValidateRowAsync(
            new ValidationPipelineRow(),
            new ValidationPipelineMetadata(),
            operation.Object,
            RowNumber,
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRowAsync_ComposedValidatorThrowsBulkValidationException_ReturnsValidationError()
    {
        var validator = new Mock<IBulkRowValidator<ValidationPipelineMetadata, ValidationPipelineRow>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationPipelineRow>(), It.IsAny<ValidationPipelineMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BulkValidationException("Field is required"));

        var pipeline = CreatePipeline(validators: [validator.Object]);
        var operation = new Mock<IBulkOperationBase<ValidationPipelineMetadata, ValidationPipelineRow>>();

        var result = await pipeline.ValidateRowAsync(
            new ValidationPipelineRow(),
            new ValidationPipelineMetadata(),
            operation.Object,
            RowNumber,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorType.Should().Be(BulkErrorType.Validation);
        result.ErrorMessage.Should().Be("Field is required");
    }

    [Fact]
    public async Task ValidateRowAsync_ComposedValidatorFails_OperationValidateRowIsNotCalled()
    {
        var validator = new Mock<IBulkRowValidator<ValidationPipelineMetadata, ValidationPipelineRow>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationPipelineRow>(), It.IsAny<ValidationPipelineMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BulkValidationException("Composed fail"));

        var operation = new Mock<IBulkOperationBase<ValidationPipelineMetadata, ValidationPipelineRow>>();
        var pipeline = CreatePipeline(validators: [validator.Object]);

        await pipeline.ValidateRowAsync(
            new ValidationPipelineRow(),
            new ValidationPipelineMetadata(),
            operation.Object,
            RowNumber,
            CancellationToken.None);

        operation.Verify(
            o => o.ValidateRowAsync(It.IsAny<ValidationPipelineRow>(), It.IsAny<ValidationPipelineMetadata>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateRowAsync_OperationValidateRowThrowsBulkValidationException_ReturnsValidationError()
    {
        var operation = new Mock<IBulkOperationBase<ValidationPipelineMetadata, ValidationPipelineRow>>();
        operation
            .Setup(o => o.ValidateRowAsync(It.IsAny<ValidationPipelineRow>(), It.IsAny<ValidationPipelineMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BulkValidationException("Invalid email"));

        var pipeline = CreatePipeline();

        var result = await pipeline.ValidateRowAsync(
            new ValidationPipelineRow(),
            new ValidationPipelineMetadata(),
            operation.Object,
            RowNumber,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorType.Should().Be(BulkErrorType.Validation);
        result.ErrorMessage.Should().Be("Invalid email");
    }

    [Fact]
    public async Task ValidateRowAsync_NonValidationException_ReturnsProcessingError()
    {
        var operation = new Mock<IBulkOperationBase<ValidationPipelineMetadata, ValidationPipelineRow>>();
        operation
            .Setup(o => o.ValidateRowAsync(It.IsAny<ValidationPipelineRow>(), It.IsAny<ValidationPipelineMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var pipeline = CreatePipeline();

        var result = await pipeline.ValidateRowAsync(
            new ValidationPipelineRow(),
            new ValidationPipelineMetadata(),
            operation.Object,
            RowNumber,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorType.Should().Be(BulkErrorType.Processing);
        result.ErrorMessage.Should().Be("Database connection failed");
    }
}

[Trait("Category", "Unit")]
public class ValidationPipelineMetadata : IBulkMetadata
{
}

[Trait("Category", "Unit")]
public class ValidationPipelineRow : IBulkRow
{
    public string? Name { get; set; }
    public string? RowId { get; set; }
}
