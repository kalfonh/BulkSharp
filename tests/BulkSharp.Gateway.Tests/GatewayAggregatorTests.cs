using BulkSharp.Gateway.Registry;
using BulkSharp.Gateway.Routing;
using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

namespace BulkSharp.Gateway.Tests;

[Trait("Category", "Unit")]
public class GatewayAggregatorTests : IDisposable
{
    private readonly Mock<IOperationRegistry> _registry = new();
    private readonly Mock<IBackendClientFactory> _clientFactory = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly GatewayAggregator _sut;

    public GatewayAggregatorTests()
    {
        var router = new GatewayRouter(
            _registry.Object,
            _clientFactory.Object,
            _cache,
            NullLogger<GatewayRouter>.Instance);

        _sut = new GatewayAggregator(
            router,
            NullLogger<GatewayAggregator>.Instance);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task AggregateDiscoveryAsync_MergesFromMultipleBackends()
    {
        var clientA = CreateMockClient("service-a");
        var clientB = CreateMockClient("service-b");

        var opsA = JsonSerializer.Serialize(new[] { new { name = "import-users" } });
        clientA.Setup(c => c.GetOperationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(opsA)
            });

        var opsB = JsonSerializer.Serialize(new[] { new { name = "import-orders" } });
        clientB.Setup(c => c.GetOperationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(opsB)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object, clientB.Object });

        var result = await _sut.AggregateDiscoveryAsync(CancellationToken.None);

        result.Should().HaveCount(2);

        // Verify sourceService tagging
        var names = result.Select(e => e.GetProperty("name").GetString()).ToList();
        names.Should().Contain("import-users");
        names.Should().Contain("import-orders");
    }

    [Fact]
    public async Task AggregateListAsync_MergesAndRepaginates()
    {
        var clientA = CreateMockClient("service-a");
        var clientB = CreateMockClient("service-b");

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var listA = JsonSerializer.Serialize(new
        {
            items = new[] { new { id = id1, createdAt = "2026-03-17T10:00:00Z", name = "op-a" } },
            totalCount = 1
        });
        clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listA)
            });

        var listB = JsonSerializer.Serialize(new
        {
            items = new[] { new { id = id2, createdAt = "2026-03-17T11:00:00Z", name = "op-b" } },
            totalCount = 1
        });
        clientB.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listB)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object, clientB.Object });

        var result = await _sut.AggregateListAsync("page=1&pageSize=20", CancellationToken.None);

        // Result is an anonymous type, use JSON round-trip to inspect
        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("TotalCount").GetInt32().Should().Be(2);

        var items = root.GetProperty("Items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        // Verify sorted by CreatedAt descending: op-b (11:00) should come first
        var firstName = items[0].GetProperty("name").GetString();
        firstName.Should().Be("op-b");
    }

    [Fact]
    public async Task AggregateListAsync_BackendDown_ReturnsPartialResults()
    {
        var clientA = CreateMockClient("service-a");
        var clientB = CreateMockClient("service-b");

        // Client A throws
        clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Client B returns data
        var listB = JsonSerializer.Serialize(new
        {
            items = new[] { new { id = Guid.NewGuid(), createdAt = "2026-03-17T10:00:00Z" } },
            totalCount = 1
        });
        clientB.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listB)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object, clientB.Object });

        var result = await _sut.AggregateListAsync("page=1&pageSize=20", CancellationToken.None);

        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Only service-b results
        root.GetProperty("TotalCount").GetInt32().Should().Be(1);
        root.GetProperty("Items").EnumerateArray().ToList().Should().HaveCount(1);
    }

    [Fact]
    public async Task AggregateListAsync_SourceMatchesBackend_RoutesToSingleBackend()
    {
        var clientA = CreateMockClient("service-a");
        var clientB = CreateMockClient("service-b");

        var listA = JsonSerializer.Serialize(new
        {
            items = new[] { new { id = Guid.NewGuid(), createdAt = "2026-03-17T10:00:00Z" } },
            totalCount = 1,
            page = 1,
            pageSize = 20
        });
        clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listA)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object, clientB.Object });

        var result = await _sut.AggregateListAsync("?source=service-a&page=1&pageSize=20", CancellationToken.None);

        // service-b should NOT have been called
        clientB.Verify(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var json = JsonSerializer.Serialize(result);
        json.Should().Contain("totalCount");
    }

    [Fact]
    public async Task AggregateListAsync_SourceNoMatch_FallsBackToFanOut()
    {
        var clientA = CreateMockClient("service-a");

        var listA = JsonSerializer.Serialize(new
        {
            items = new[] { new { id = Guid.NewGuid(), createdAt = "2026-03-17T10:00:00Z" } },
            totalCount = 1
        });
        clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listA)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object });

        var result = await _sut.AggregateListAsync("?source=unknown-service&page=1&pageSize=20", CancellationToken.None);

        clientA.Verify(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AggregateListAsync_SourceStrippedFromForwardedQueryString()
    {
        var clientA = CreateMockClient("service-a");

        var listA = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            totalCount = 0,
            page = 1,
            pageSize = 10
        });
        clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listA)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object });

        await _sut.AggregateListAsync("?source=service-a&page=2&pageSize=10", CancellationToken.None);

        clientA.Verify(c => c.GetBulksAsync(
            It.Is<string>(qs => qs.Contains("page=2") && qs.Contains("pageSize=10") && !qs.Contains("source")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AggregateListAsync_SourceOnlyParam_ForwardsEmptyQueryString()
    {
        var clientA = CreateMockClient("service-a");

        var listA = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            totalCount = 0,
            page = 1,
            pageSize = 20
        });
        clientA.Setup(c => c.GetBulksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listA)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object });

        await _sut.AggregateListAsync("?source=service-a", CancellationToken.None);

        clientA.Verify(c => c.GetBulksAsync(
            It.Is<string>(qs => qs == ""),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IBackendClient> CreateMockClient(string serviceName)
    {
        var mock = new Mock<IBackendClient>();
        mock.Setup(c => c.ServiceName).Returns(serviceName);
        return mock;
    }
}
