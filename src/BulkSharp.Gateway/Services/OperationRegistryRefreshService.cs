using System.Text.Json;
using BulkSharp.Gateway.Configuration;
using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BulkSharp.Gateway.Services;

internal sealed class OperationRegistryRefreshService : BackgroundService
{
    private readonly IBackendClientFactory _clientFactory;
    private readonly IOperationRegistry _registry;
    private readonly BulkSharpGatewayOptions _options;
    private readonly ILogger<OperationRegistryRefreshService> _logger;

    public OperationRegistryRefreshService(
        IBackendClientFactory clientFactory,
        IOperationRegistry registry,
        BulkSharpGatewayOptions options,
        ILogger<OperationRegistryRefreshService> logger)
    {
        _clientFactory = clientFactory;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial refresh on startup
        await RefreshAsync(stoppingToken);

        // Periodic refresh
        using var timer = new PeriodicTimer(_options.RegistryRefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        _logger.RefreshingRegistry(_options.Backends.Count);

        foreach (var backend in _options.Backends)
        {
            try
            {
                var client = _clientFactory.GetClient(backend.Name);
                using var response = await client.GetOperationsAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.BackendDiscoveryFailed(backend.Name, response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                var operations = JsonSerializer.Deserialize<List<DiscoveredOperation>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (operations != null)
                {
                    var names = operations.Select(o => o.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
                    _registry.UpdateOperations(backend.Name, names!);
                    _logger.BackendOperationsRegistered(backend.Name, names.Count);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("registered by both"))
            {
                _logger.DuplicateOperationDetected(ex);
                throw; // Fail-fast on duplicate names
            }
            catch (Exception ex)
            {
                _logger.BackendRefreshFailed(ex, backend.Name);
            }
        }
    }

    private sealed class DiscoveredOperation
    {
        public string? Name { get; set; }
    }
}
