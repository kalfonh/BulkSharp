using BulkSharp.Gateway.Registry;
using BulkSharp.Gateway.Routing;
using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

namespace BulkSharp.Gateway.Tests;

[Trait("Category", "Unit")]
public class GatewayRouterTests : IDisposable
{
    private readonly Mock<IOperationRegistry> _registry = new();
    private readonly Mock<IBackendClientFactory> _clientFactory = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly GatewayRouter _sut;

    public GatewayRouterTests()
    {
        _sut = new GatewayRouter(
            _registry.Object,
            _clientFactory.Object,
            _cache,
            NullLogger<GatewayRouter>.Instance);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void RouteByOperation_Known_ReturnsClient()
    {
        var mockClient = CreateMockClient("service-a");
        _registry.Setup(r => r.LookupService("import-users")).Returns("service-a");
        _clientFactory.Setup(f => f.GetClient("service-a")).Returns(mockClient.Object);

        var result = _sut.RouteByOperation("import-users");

        result.Should().NotBeNull();
        result!.ServiceName.Should().Be("service-a");
    }

    [Fact]
    public void RouteByOperation_Unknown_ReturnsNull()
    {
        _registry.Setup(r => r.LookupService("unknown")).Returns((string?)null);

        var result = _sut.RouteByOperation("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RouteBySourceServiceAsync_CacheHit_ReturnsClient()
    {
        var opId = Guid.NewGuid();
        var mockClient = CreateMockClient("service-a");
        _cache.Set($"op:{opId}", "service-a");
        _clientFactory.Setup(f => f.GetClient("service-a")).Returns(mockClient.Object);

        var result = await _sut.RouteBySourceServiceAsync(opId);

        result.Should().NotBeNull();
        result!.ServiceName.Should().Be("service-a");
        // Should NOT have called GetAllClients (no fan-out needed)
        _clientFactory.Verify(f => f.GetAllClients(), Times.Never);
    }

    [Fact]
    public async Task RouteBySourceServiceAsync_CacheMiss_FansOut()
    {
        var opId = Guid.NewGuid();
        var clientA = CreateMockClient("service-a");
        var clientB = CreateMockClient("service-b");

        // Client A returns 404
        clientA.Setup(c => c.GetBulkAsync(opId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        // Client B returns 200 with source
        var responseBody = JsonSerializer.Serialize(new { id = opId, source = "service-b" });
        clientB.Setup(c => c.GetBulkAsync(opId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object, clientB.Object });
        _clientFactory.Setup(f => f.GetClient("service-b")).Returns(clientB.Object);

        var result = await _sut.RouteBySourceServiceAsync(opId);

        result.Should().NotBeNull();
        result!.ServiceName.Should().Be("service-b");

        // Verify cache was populated
        _cache.TryGetValue($"op:{opId}", out string? cached).Should().BeTrue();
        cached.Should().Be("service-b");
    }

    [Fact]
    public async Task RouteBySourceServiceAsync_AllBackendsFail_ReturnsNull()
    {
        var opId = Guid.NewGuid();
        var clientA = CreateMockClient("service-a");
        clientA.Setup(c => c.GetBulkAsync(opId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        _clientFactory.Setup(f => f.GetAllClients())
            .Returns(new[] { clientA.Object });

        var result = await _sut.RouteBySourceServiceAsync(opId);

        result.Should().BeNull();
    }

    private static Mock<IBackendClient> CreateMockClient(string serviceName)
    {
        var mock = new Mock<IBackendClient>();
        mock.Setup(c => c.ServiceName).Returns(serviceName);
        return mock;
    }
}
