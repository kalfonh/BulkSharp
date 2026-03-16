using BulkSharp.Core.Exceptions;
using BulkSharp.Processing.DataFormats;
using System.Text;

namespace BulkSharp.UnitTests.DataFormats;

[Trait("Category", "Unit")]
public class CsvSchemaAttributeTests
{
    [Fact]
    public async Task ProcessAsync_SemicolonDelimiter_ParsesCorrectly()
    {
        // Arrange
        var processor = new CsvDataFormatProcessor<SemicolonRow>();
        var csv = "Name;Value\nAlice;42";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var results = new List<SemicolonRow>();
        await foreach (var row in processor.ProcessAsync(stream))
        {
            results.Add(row);
        }

        // Assert
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Alice");
        results[0].Value.Should().Be(42);
    }

    [Fact]
    public async Task ProcessAsync_NoHeader_ParsesByIndex()
    {
        // Arrange
        var processor = new CsvDataFormatProcessor<NoHeaderRow>();
        var csv = "Bob,99";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var results = new List<NoHeaderRow>();
        await foreach (var row in processor.ProcessAsync(stream))
        {
            results.Add(row);
        }

        // Assert
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Bob");
        results[0].Value.Should().Be(99);
    }

    [Fact]
    public async Task ProcessAsync_MissingRequiredColumn_ThrowsBulkValidationException()
    {
        // Arrange
        var processor = new CsvDataFormatProcessor<RequiredColumnRow>();
        var csv = "full_name\nAlice";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var act = async () =>
        {
            await foreach (var _ in processor.ProcessAsync(stream)) { }
        };

        // Assert
        var ex = await act.Should().ThrowAsync<BulkValidationException>();
        ex.Which.Message.Should().Contain("email_address");
    }

    [Fact]
    public async Task ProcessAsync_OptionalColumnMissing_Succeeds()
    {
        // Arrange
        var processor = new CsvDataFormatProcessor<OptionalColumnRow>();
        var csv = "full_name\nAlice";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var results = new List<OptionalColumnRow>();
        await foreach (var row in processor.ProcessAsync(stream))
        {
            results.Add(row);
        }

        // Assert
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Alice");
    }

    [CsvSchema(Delimiter = ";")]
    public class SemicolonRow : IBulkRow
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public string? RowId { get; set; }
    }

    [CsvSchema(HasHeaderRecord = false)]
    public class NoHeaderRow : IBulkRow
    {
        [CsvColumn(0)]
        public string Name { get; set; } = string.Empty;

        [CsvColumn(1)]
        public int Value { get; set; }

        public string? RowId { get; set; }
    }

    public class RequiredColumnRow : IBulkRow
    {
        [CsvColumn("full_name")]
        public string Name { get; set; } = string.Empty;

        [CsvColumn("email_address")]
        public string Email { get; set; } = string.Empty;

        public string? RowId { get; set; }
    }

    public class OptionalColumnRow : IBulkRow
    {
        [CsvColumn("full_name")]
        public string Name { get; set; } = string.Empty;

        [CsvColumn("nickname", Required = false)]
        public string? Nickname { get; set; }

        public string? RowId { get; set; }
    }
}
