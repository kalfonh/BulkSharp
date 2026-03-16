using BulkSharp.Core.Abstractions.DataFormats;
using BulkSharp.Processing.DataFormats;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class DataFormatProcessorFactoryTests
{
    [Fact]
    public void GetProcessor_UnsupportedExtension_ThrowsNotSupportedException()
    {
        // Arrange
        var processors = Enumerable.Empty<IDataFormatProcessor<DataFormatTestRow>>();
        var factory = new DataFormatProcessorFactory<DataFormatTestRow>(processors);

        // Act & Assert
        var act = () => factory.GetProcessor("data.xml");
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*data.xml*");
    }

    [Fact]
    public void GetProcessor_SupportedExtension_ReturnsProcessor()
    {
        // Arrange
        var mockProcessor = new Mock<IDataFormatProcessor<DataFormatTestRow>>();
        mockProcessor.Setup(p => p.SupportedFormat).Returns("csv");

        var factory = new DataFormatProcessorFactory<DataFormatTestRow>(new[] { mockProcessor.Object });

        // Act
        var result = factory.GetProcessor("users.csv");

        // Assert
        result.Should().BeSameAs(mockProcessor.Object);
    }

    [Fact]
    public void GetProcessor_ExtensionMatchIsCaseInsensitive()
    {
        // Arrange
        var mockProcessor = new Mock<IDataFormatProcessor<DataFormatTestRow>>();
        mockProcessor.Setup(p => p.SupportedFormat).Returns("csv");

        var factory = new DataFormatProcessorFactory<DataFormatTestRow>(new[] { mockProcessor.Object });

        // Act
        var result = factory.GetProcessor("USERS.CSV");

        // Assert
        result.Should().BeSameAs(mockProcessor.Object);
    }

    [Fact]
    public void SupportedFormats_ReturnsAllRegisteredFormats()
    {
        // Arrange
        var csvProcessor = new Mock<IDataFormatProcessor<DataFormatTestRow>>();
        csvProcessor.Setup(p => p.SupportedFormat).Returns("csv");

        var jsonProcessor = new Mock<IDataFormatProcessor<DataFormatTestRow>>();
        jsonProcessor.Setup(p => p.SupportedFormat).Returns("json");

        var factory = new DataFormatProcessorFactory<DataFormatTestRow>(new[] { csvProcessor.Object, jsonProcessor.Object });

        // Act & Assert
        factory.SupportedFormats.Should().BeEquivalentTo(new[] { "csv", "json" });
    }

}

[Trait("Category", "Unit")]
public class DataFormatTestRow : IBulkRow
{
    public string Name { get; set; } = string.Empty;
    public string? RowId { get; set; }
}
