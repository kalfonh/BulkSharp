using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Registry;
using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("source", out var sourceProp) ||
                        doc.RootElement.TryGetProperty("Source", out sourceProp))
                    {
                        var source = sourceProp.GetString();
                        if (!string.IsNullOrEmpty(source))
                        {
                            _sourceCache.Set($"op:{operationId}", source, CacheOptions);
                            return (Client: client, Found: true, Source: source);
                        }
                    }
                    // Source not set - use the backend that returned 200
                    _sourceCache.Set($"op:{operationId}", client.ServiceName, CacheOptions);
                    return (Client: client, Found: true, Source: client.ServiceName);
                }
            }
            catch (Exception ex)
            {
                _logger.RouterFanOutFailed(ex, client.ServiceName, operationId);
            }
            return (Client: client, Found: false, Source: (string?)null);
        });

        var results = await Task.WhenAll(tasks);
        var winner = results.FirstOrDefault(r => r.Found);

        if (winner.Found && winner.Source != null)
            return _clientFactory.GetClient(winner.Source);

        return null;
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
