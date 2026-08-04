using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BulkSharp.Gateway.HealthChecks;

/// <summary>
/// Reports the reachability of every registered BulkSharp backend.
/// </summary>
/// <remarks>
/// Degraded rather than unhealthy when only some backends respond: the gateway still
/// correctly serves the operations owned by the reachable ones, and failing the liveness
/// probe would have the orchestrator replace a gateway that is working as designed.
/// </remarks>
/// <param name="clientFactory">Factory supplying the configured backend clients.</param>
public sealed class BackendHealthCheck(IBackendClientFactory clientFactory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var clients = clientFactory.GetAllClients().ToList();
        if (clients.Count == 0)
            return HealthCheckResult.Unhealthy("No backends are configured.");

        var failures = new Dictionary<string, object>();

        foreach (var client in clients)
        {
            try
            {
                using var response = await client.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    failures[client.ServiceName] = (int)response.StatusCode;
            }
            catch (Exception ex)
            {
                failures[client.ServiceName] = ex.Message;
            }
        }

        if (failures.Count == 0)
            return HealthCheckResult.Healthy();

        return failures.Count == clients.Count
            ? HealthCheckResult.Unhealthy("No backends are reachable.", data: failures)
            : HealthCheckResult.Degraded("Some backends are unreachable.", data: failures);
    }
}
