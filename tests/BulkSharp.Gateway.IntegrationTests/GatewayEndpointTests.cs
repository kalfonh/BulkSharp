using System.Net;
using System.Text;
using System.Text.Json;

namespace BulkSharp.Gateway.IntegrationTests;

[Trait("Category", "Integration")]
public class GatewayEndpointTests : IDisposable
{
    private readonly GatewayTestFixture _fixture = new();

    [Fact]
    public async Task Discovery_AggregatesFromMultipleBackends()
    {
        var backend1 = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "op-a", Description = "Service A op" } });
        var backend2 = new FakeBackendHandler("svc-b")
            .OnGet("api/operations", new[] { new { Name = "op-b", Description = "Service B op" } });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend1), ("svc-b", backend2));

        var response = await client.GetAsync("/api/operations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var ops = JsonSerializer.Deserialize<JsonElement[]>(json);
        ops.Should().NotBeNull();
        ops!.Length.Should().Be(2);
    }

    [Fact]
    public async Task GetBulk_RoutesToCorrectBackend()
    {
        var opId = Guid.NewGuid();
        var backend = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "test-op" } })
            .OnGet($"api/bulks/{opId}", new { Id = opId, OperationName = "test-op", Source = "svc-a", Status = 0 });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend));

        var response = await client.GetAsync($"/api/bulks/{opId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain(opId.ToString());
    }

    [Fact]
    public async Task GetBulk_NotFound_Returns404()
    {
        var opId = Guid.NewGuid();
        var backend = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", Array.Empty<object>())
            .OnGetReturn($"api/bulks/{opId}", HttpStatusCode.NotFound);

        using var client = _fixture.CreateGatewayClient(("svc-a", backend));

        var response = await client.GetAsync($"/api/bulks/{opId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AggregatedList_MergesFromMultipleBackends()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var backend1 = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "op-a" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = id1.ToString(), OperationName = "op-a", CreatedAt = "2026-03-17T10:00:00Z", Source = "svc-a", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });
        var backend2 = new FakeBackendHandler("svc-b")
            .OnGet("api/operations", new[] { new { Name = "op-b" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = id2.ToString(), OperationName = "op-b", CreatedAt = "2026-03-17T11:00:00Z", Source = "svc-b", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend1), ("svc-b", backend2));

        var response = await client.GetAsync("/api/bulks?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        GetJsonProperty(root, "Items", "items").GetArrayLength().Should().Be(2);
        GetJsonProperty(root, "TotalCount", "totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task AggregatedList_SortsByCreatedAtDescending()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var backend1 = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "op-early" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = id1.ToString(), OperationName = "op-early", CreatedAt = "2026-03-17T08:00:00Z", Source = "svc-a", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });
        var backend2 = new FakeBackendHandler("svc-b")
            .OnGet("api/operations", new[] { new { Name = "op-late" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = id2.ToString(), OperationName = "op-late", CreatedAt = "2026-03-17T18:00:00Z", Source = "svc-b", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend1), ("svc-b", backend2));

        var response = await client.GetAsync("/api/bulks?page=1&pageSize=20");
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var items = GetJsonProperty(doc.RootElement, "Items", "items");

        // Later item should appear first (descending order)
        var firstOp = GetJsonProperty(items[0], "operationName", "OperationName").GetString();
        firstOp.Should().Be("op-late");
    }

    [Fact]
    public async Task BackendDown_PartialResultsReturned()
    {
        var healthyBackend = new FakeBackendHandler("healthy")
            .OnGet("api/operations", new[] { new { Name = "op-healthy" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = Guid.NewGuid().ToString(), OperationName = "op-healthy", CreatedAt = "2026-03-17T10:00:00Z", Source = "healthy", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });

        // Sick backend returns 404 for everything (simulating down/unreachable)
        var sickBackend = new FakeBackendHandler("sick")
            .OnGet("api/operations", Array.Empty<object>());

        using var client = _fixture.CreateGatewayClient(("healthy", healthyBackend), ("sick", sickBackend));

        var response = await client.GetAsync("/api/bulks?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        GetJsonProperty(doc.RootElement, "TotalCount", "totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetBulkStatus_RoutesAndProxies()
    {
        var opId = Guid.NewGuid();
        var backend = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "test-op" } })
            .OnGet($"api/bulks/{opId}", new { Id = opId, OperationName = "test-op", Source = "svc-a", Status = 0 })
            .OnGet($"api/bulks/{opId}/status", new { Id = opId, Status = "Completed", Progress = 100 });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend));

        // First call primes the source cache via fan-out
        await client.GetAsync($"/api/bulks/{opId}");

        var response = await client.GetAsync($"/api/bulks/{opId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Completed");
    }

    [Fact]
    public async Task GetBulkErrors_RoutesAndProxies()
    {
        var opId = Guid.NewGuid();
        var backend = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "test-op" } })
            .OnGet($"api/bulks/{opId}", new { Id = opId, OperationName = "test-op", Source = "svc-a", Status = 0 })
            .OnGet($"api/bulks/{opId}/errors", new
            {
                Items = new[] { new { Row = 5, Error = "Invalid email" } },
                TotalCount = 1
            });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend));

        // Prime cache
        await client.GetAsync($"/api/bulks/{opId}");

        var response = await client.GetAsync($"/api/bulks/{opId}/errors?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Invalid email");
    }

    [Fact]
    public async Task GetBulkRows_RoutesAndProxies()
    {
        var opId = Guid.NewGuid();
        var backend = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "test-op" } })
            .OnGet($"api/bulks/{opId}", new { Id = opId, OperationName = "test-op", Source = "svc-a", Status = 0 })
            .OnGet($"api/bulks/{opId}/rows", new
            {
                Items = new[] { new { RowIndex = 0, Status = "Processed" } },
                TotalCount = 1
            });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend));

        // Prime cache
        await client.GetAsync($"/api/bulks/{opId}");

        var response = await client.GetAsync($"/api/bulks/{opId}/rows?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Processed");
    }

    [Fact]
    public async Task PostCancel_RoutesAndProxies()
    {
        var opId = Guid.NewGuid();
        var backend = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "test-op" } })
            .OnGet($"api/bulks/{opId}", new { Id = opId, OperationName = "test-op", Source = "svc-a", Status = 0 })
            .OnPost($"api/bulks/{opId}/cancel", new { Success = true });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend));

        // Prime cache
        await client.GetAsync($"/api/bulks/{opId}");

        var response = await client.PostAsync($"/api/bulks/{opId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("true");
    }

    [Fact]
    public async Task Discovery_TagsSourceService()
    {
        var backend = new FakeBackendHandler("my-service")
            .OnGet("api/operations", new[] { new { Name = "op-x", Description = "Test" } });

        using var client = _fixture.CreateGatewayClient(("my-service", backend));

        var response = await client.GetAsync("/api/operations");
        var json = await response.Content.ReadAsStringAsync();

        // The aggregator should tag each operation with sourceService
        json.Should().Contain("sourceService");
        json.Should().Contain("my-service");
    }

    [Fact]
    public async Task GetBulk_FanOutFindsCorrectBackend()
    {
        var opId = Guid.NewGuid();

        // Backend A does not have this operation
        var backendA = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "op-a" } })
            .OnGetReturn($"api/bulks/{opId}", HttpStatusCode.NotFound);

        // Backend B has it
        var backendB = new FakeBackendHandler("svc-b")
            .OnGet("api/operations", new[] { new { Name = "op-b" } })
            .OnGet($"api/bulks/{opId}", new { Id = opId, OperationName = "op-b", Source = "svc-b", Status = 0 });

        using var client = _fixture.CreateGatewayClient(("svc-a", backendA), ("svc-b", backendB));

        // Fan-out should find it on svc-b
        var response = await client.GetAsync($"/api/bulks/{opId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain(opId.ToString());
        json.Should().Contain("svc-b");
    }

    [Fact]
    public async Task AggregatedList_WithSource_RoutesToSingleBackend()
    {
        var id1 = Guid.NewGuid();

        var backend1 = new FakeBackendHandler("svc-a")
            .OnGet("api/operations", new[] { new { Name = "op-a" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = id1.ToString(), OperationName = "op-a", CreatedAt = "2026-03-17T10:00:00Z", Source = "svc-a", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });

        // backend2 has data but should NOT be contacted
        var backend2 = new FakeBackendHandler("svc-b")
            .OnGet("api/operations", new[] { new { Name = "op-b" } })
            .OnGet("api/bulks", new
            {
                Items = new[] { new { Id = Guid.NewGuid().ToString(), OperationName = "op-b", CreatedAt = "2026-03-17T11:00:00Z", Source = "svc-b", Status = 0 } },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            });

        using var client = _fixture.CreateGatewayClient(("svc-a", backend1), ("svc-b", backend2));

        var response = await client.GetAsync("/api/bulks?source=svc-a&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        // Should only contain svc-a's operation
        var totalCount = doc.RootElement.GetProperty("totalCount").GetInt32();
        totalCount.Should().Be(1);

        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1);
        items[0].GetProperty("operationName").GetString().Should().Be("op-a");
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Gets a JSON property trying multiple casing variants (camelCase, PascalCase).
    /// ASP.NET minimal APIs serialize with camelCase by default, but the gateway
    /// proxies raw JSON from backends which may use PascalCase.
    /// </summary>
    private static JsonElement GetJsonProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
                return prop;
        }
        throw new KeyNotFoundException($"None of the property names [{string.Join(", ", names)}] found in JSON element.");
    }
}
