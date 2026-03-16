using System.Text;
using System.Text.Json;
using BulkSharp.Processing.DataFormats;
using BulkSharp.Core.Abstractions.Processing;

namespace BulkSharp.UnitTests.Security;

[Trait("Category", "Unit")]
public class DeserializationSecurityTests
{
    [Fact]
    public async Task JsonProcessor_WithDeepNesting_ThrowsJsonException()
    {
        var processor = new JsonDataFormatProcessor<Dictionary<string, object>>();
        var deepJson = CreateDeeplyNestedJson(50);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(deepJson));

        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await foreach (var _ in processor.ProcessAsync(stream)) { }
        });
    }

    [Fact]
    public async Task JsonProcessor_WithValidDepth_Succeeds()
    {
        var processor = new JsonDataFormatProcessor<TestDeserializationRow>();
        var json = """[{"Name": "Test", "Value": 123}]""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = new List<TestDeserializationRow>();
        await foreach (var item in processor.ProcessAsync(stream))
            result.Add(item);

        Assert.Single(result);
        Assert.Equal("Test", result[0].Name);
    }

    [Fact]
    public async Task JsonProcessor_StreamingWithValidData_YieldsAllItems()
    {
        var processor = new JsonDataFormatProcessor<TestDeserializationRow>();
        var json = """[{"Name": "A", "Value": 1},{"Name": "B", "Value": 2}]""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var items = new List<TestDeserializationRow>();
        await foreach (var item in processor.ProcessAsync(stream))
            items.Add(item);

        Assert.Equal(2, items.Count);
    }

    private static string CreateDeeplyNestedJson(int depth)
    {
        var json = "{}";
        for (int i = 0; i < depth; i++)
        {
            json = $"{{\"level{i}\": {json}}}";
        }
        return $"[{json}]";
    }

    public class TestDeserializationRow : IBulkRow
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string? RowId { get; set; }
    }
}
