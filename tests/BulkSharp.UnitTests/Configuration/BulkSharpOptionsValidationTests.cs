using BulkSharp.Core.Configuration;

namespace BulkSharp.UnitTests.Configuration;

[Trait("Category", "Unit")]
public class BulkSharpOptionsValidationTests
{
    [Fact]
    public void Validate_DefaultOptions_DoesNotThrow()
    {
        var options = new BulkSharpOptions();

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_FlushBatchSizeZero_Throws()
    {
        var options = new BulkSharpOptions { FlushBatchSize = 0 };

        var act = () => options.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(BulkSharpOptions.FlushBatchSize));
    }

    [Fact]
    public void Validate_FlushBatchSizeNegative_Throws()
    {
        var options = new BulkSharpOptions { FlushBatchSize = -1 };

        var act = () => options.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(BulkSharpOptions.FlushBatchSize));
    }

    [Fact]
    public void Validate_MaxFileSizeBytesNegative_Throws()
    {
        var options = new BulkSharpOptions { MaxFileSizeBytes = -1 };

        var act = () => options.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(BulkSharpOptions.MaxFileSizeBytes));
    }

    [Fact]
    public void Validate_MaxFileSizeBytesZero_DoesNotThrow()
    {
        var options = new BulkSharpOptions { MaxFileSizeBytes = 0 };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }
}
