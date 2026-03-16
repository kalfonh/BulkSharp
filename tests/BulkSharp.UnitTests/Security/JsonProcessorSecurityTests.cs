using System.Text;
using System.Text.Json;
using BulkSharp.Processing.DataFormats;
using BulkSharp.Core.Abstractions.Processing;

namespace BulkSharp.UnitTests.Security;

[Trait("Category", "Unit")]
public class JsonProcessorSecurityTests
{
    [Fact]
    public async Task ProcessAsync_DeeplyNestedJson_ThrowsJsonException()
    {
        var processor = new JsonDataFormatProcessor<Dictionary<string, object>>();
        var json = CreateDeeplyNestedJson(50);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await foreach (var _ in processor.ProcessAsync(stream)) { }
        });
    }

    [Fact]
    public async Task ProcessAsync_ValidJson_StreamsCorrectly()
    {
        var processor = new JsonDataFormatProcessor<TestJsonRow>();
        var json = """[{"Name":"Alice"},{"Name":"Bob"}]""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var items = new List<TestJsonRow>();
        await foreach (var item in processor.ProcessAsync(stream))
            items.Add(item);

        Assert.Equal(2, items.Count);
        Assert.Equal("Alice", items[0].Name);
        Assert.Equal("Bob", items[1].Name);
    }

    private static string CreateDeeplyNestedJson(int depth)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++) sb.Append("{\"nested\":");
        sb.Append("1");
        for (int i = 0; i < depth; i++) sb.Append('}');
        return $"[{sb}]";
    }

    public class TestJsonRow : IBulkRow
    {
        public string Name { get; set; } = "";
        public string? RowId { get; set; }
    }
}
