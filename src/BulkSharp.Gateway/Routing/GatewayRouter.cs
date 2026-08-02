using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Registry;
using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BulkSharp.Gateway.Routing;

public sealed class GatewayRouter
{
    private readonly IOperationRegistry _registry;
    private readonly IBackendClientFactory _clientFactory;
    private readonly IMemoryCache _sourceCache;
    private readonly ILogger<GatewayRouter> _logger;
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(1)
    };

    public GatewayRouter(
        IOperationRegistry registry,
        IBackendClientFactory clientFactory,
        IMemoryCache sourceCache,
        ILogger<GatewayRouter> logger)
    {
        _registry = registry;
        _clientFactory = clientFactory;
        _sourceCache = sourceCache;
        _logger = logger;
    }

    public IBackendClient? RouteByOperation(string operationName)
    {
        var service = _registry.LookupService(operationName);
        return service != null ? _clientFactory.GetClient(service) : null;
    }

    public async Task<IBackendClient?> RouteBySourceServiceAsync(Guid operationId, CancellationToken ct = default)
    {
        // Check cache
        if (_sourceCache.TryGetValue($"op:{operationId}", out string? cachedService) && cachedService != null)
            return _clientFactory.GetClient(cachedService);

        // Cache miss: fan out to all backends
        _logger.RouterCacheMiss(operationId);

        var clients = _clientFactory.GetAllClients().ToList();
        var tasks = clients.Select(async client =>
        {
            try
            {
                using var response = await client.GetBulkAsync(operationId, ct);
                if (response.IsSuccessStatusCode)
                    return (Client: client, Found: true);
            }
            catch (Exception ex)
            {
                _logger.RouterFanOutFailed(ex, client.ServiceName, operationId);
            }
            return (Client: client, Found: false);
        });

        var results = await Task.WhenAll(tasks);
        var winner = results.FirstOrDefault(r => r.Found);

        if (!winner.Found)
            return null;

        // Route by the backend that actually answered, never by the Source value it
        // reports. Source is the backend's own BulkSharpOptions.ServiceName, which need
        // not match the name this gateway was configured with in AddBackend(). Resolving
        // an unconfigured name produces an HttpClient with no BaseAddress, and every
        // subsequent request for this operation throws.
        _sourceCache.Set($"op:{operationId}", winner.Client.ServiceName, CacheOptions);
        return winner.Client;
    }

    public void CacheSource(Guid operationId, string serviceName)
    {
        _sourceCache.Set($"op:{operationId}", serviceName, CacheOptions);
    }

    public IEnumerable<IBackendClient> GetAllClients() => _clientFactory.GetAllClients();

    public IBackendClient? GetClientByServiceName(string serviceName)
    {
        return _clientFactory.GetAllClients()
            .FirstOrDefault(c => string.Equals(c.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
    }
}
