using System.Text.Json;
using BulkSharp.Gateway.Configuration;
using BulkSharp.Gateway.Logging;
using BulkSharp.Gateway.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BulkSharp.Gateway.Services;

internal sealed class OperationRegistryRefreshService(
    IBackendClientFactory clientFactory,
    IOperationRegistry registry,
    BulkSharpGatewayOptions options,
    ILogger<OperationRegistryRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial refresh on startup
        await RefreshAsync(stoppingToken);

        // Periodic refresh
        using var timer = new PeriodicTimer(options.RegistryRefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        logger.RefreshingRegistry(options.Backends.Count);

        foreach (var backend in options.Backends)
        {
            try
            {
                var client = clientFactory.GetClient(backend.Name);
                using var response = await client.GetOperationsAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    logger.BackendDiscoveryFailed(backend.Name, response.StatusCode);
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
                    registry.UpdateOperations(backend.Name, names!);
                    logger.BackendOperationsRegistered(backend.Name, names.Count);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("registered by both"))
            {
                logger.DuplicateOperationDetected(ex);
                throw; // Fail-fast on duplicate names
            }
            catch (Exception ex)
            {
                logger.BackendRefreshFailed(ex, backend.Name);
            }
        }
    }

    private sealed class DiscoveredOperation
    {
        public string? Name { get; set; }
    }
}
