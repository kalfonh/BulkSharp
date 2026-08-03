using System.Text.Json;
using BulkSharp.Api;
using BulkSharp.Core.Abstractions.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BulkSharp.Api.Tests;

[Trait("Category", "Integration")]
public class OpenApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private JsonDocument _doc = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddBulkSharp(b =>
        {
            b.UseFileStorage(fs => fs.UseInMemory())
             .UseMetadataStorage(ms => ms.UseInMemory())
             .UseScheduler(s => s.UseImmediate());
        });
        builder.Services.AddBulkSharpEndpoints();

        _app = builder.Build();
        _app.MapBulkSharpOpenApi();
        _app.MapBulkSharpEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        var json = await _client.GetStringAsync("/openapi/v1.json");
        _doc = JsonDocument.Parse(json);
    }

    public async Task DisposeAsync()
    {
        _doc.Dispose();
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/operations")]
    [InlineData("/api/operations/{name}/template")]
    [InlineData("/api/bulks")]
    [InlineData("/api/bulks/{id}")]
    [InlineData("/api/bulks/{id}/status")]
    [InlineData("/api/bulks/{id}/errors")]
    [InlineData("/api/bulks/{id}/rows")]
    [InlineData("/api/bulks/{id}/file")]
    [InlineData("/api/bulks/{id}/export")]
    [InlineData("/api/bulks/{id}/cancel")]
    [InlineData("/api/bulks/{id}/retry")]
    [InlineData("/api/bulks/{id}/retry/rows")]
    [InlineData("/api/bulks/{id}/retry/eligibility")]
    [InlineData("/api/bulks/{id}/retry/history")]
    [InlineData("/api/bulks/validate")]
    public void Document_DescribesEndpoint(string path)
    {
        var paths = _doc.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(path, out _),
            $"OpenAPI document is missing '{path}'. Present: {string.Join(", ", paths.EnumerateObject().Select(p => p.Name))}");
    }

    /// <summary>
    /// Without a stable operationId on every verb, a generated client gets machine-derived
    /// method names that churn on each regeneration, producing spurious diffs and broken
    /// call sites in the consuming application.
    /// </summary>
    [Fact]
    public void Document_NamesEveryOperation()
    {
        var missing = new List<string>();

        foreach (var path in _doc.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var verb in path.Value.EnumerateObject())
            {
                if (!verb.Value.TryGetProperty("operationId", out var opId) ||
                    string.IsNullOrWhiteSpace(opId.GetString()))
                {
                    missing.Add($"{verb.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"Verbs without an operationId: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Document_OperationIdsAreUnique()
    {
        var ids = new List<string>();

        foreach (var path in _doc.RootElement.GetProperty("paths").EnumerateObject())
            foreach (var verb in path.Value.EnumerateObject())
                if (verb.Value.TryGetProperty("operationId", out var opId))
                    ids.Add(opId.GetString()!);

        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate operationIds: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// The document must describe enums the way the API actually serializes them — as
    /// strings. If it says integer, a generated client sends and expects integers and
    /// every enum-valued call fails at runtime, with the document appearing correct.
    /// </summary>
    [Fact]
    public async Task Document_DescribesEnumsAsStrings_MatchingTheWireFormat()
    {
        var schemas = _doc.RootElement.GetProperty("components").GetProperty("schemas");
        var statusProperty = schemas
            .GetProperty("BulkStatusDto")
            .GetProperty("properties")
            .GetProperty("status");

        // Follow a $ref if the generator emitted one rather than inlining.
        var schema = statusProperty;
        if (statusProperty.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];
            schema = schemas.GetProperty(name);
        }
        else if (statusProperty.TryGetProperty("allOf", out var allOf))
        {
            var name = allOf.EnumerateArray().First().GetProperty("$ref").GetString()!.Split('/')[^1];
            schema = schemas.GetProperty(name);
        }

        Assert.Equal("string", schema.GetProperty("type").GetString());

        // And prove the document agrees with a real response body.
        using var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "openapi-wire-probe",
            Status = BulkOperationStatus.Running,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        await repo.CreateAsync(operation);

        var body = await _client.GetStringAsync($"/api/bulks/{operation.Id}/status");

        Assert.Contains("\"status\":\"Running\"", body);
    }

    [Fact]
    public void Document_DeclaresTypedResponseSchemas()
    {
        var status = _doc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/bulks/{id}/status")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200");

        Assert.True(
            status.TryGetProperty("content", out _),
            "GET /api/bulks/{id}/status declares no response schema, so a generated client returns an untyped body.");
    }
}
