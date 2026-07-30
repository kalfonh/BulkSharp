using System.Text.Json;
using BulkSharp.Core.Contracts;

namespace BulkSharp.UnitTests.Contracts;

[Trait("Category", "Unit")]
public class BulkSharpJsonSerializationTests
{
    private sealed record Probe(BulkOperationStatus Status, int TotalRows, string? ErrorMessage);

    [Fact]
    public void Options_SerializeEnumsAsStrings()
    {
        var json = JsonSerializer.Serialize(
            new Probe(BulkOperationStatus.Running, 5, null),
            BulkSharpJsonSerialization.Options);

        json.Should().Contain("\"status\":\"Running\"");
    }

    [Fact]
    public void Options_UseCamelCasePropertyNames()
    {
        var json = JsonSerializer.Serialize(
            new Probe(BulkOperationStatus.Pending, 5, null),
            BulkSharpJsonSerialization.Options);

        json.Should().Contain("\"totalRows\":5");
    }

    [Fact]
    public void Options_OmitNullProperties()
    {
        var json = JsonSerializer.Serialize(
            new Probe(BulkOperationStatus.Pending, 5, null),
            BulkSharpJsonSerialization.Options);

        json.Should().NotContain("errorMessage");
    }

    [Fact]
    public void Configure_IsIdempotent()
    {
        var options = new JsonSerializerOptions();

        BulkSharpJsonSerialization.Configure(options);
        BulkSharpJsonSerialization.Configure(options);

        options.Converters.Count(c => c is System.Text.Json.Serialization.JsonStringEnumConverter)
            .Should().Be(1);
    }

    [Fact]
    public void Configure_Throws_WhenOptionsAreNull()
    {
        var act = () => BulkSharpJsonSerialization.Configure(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
