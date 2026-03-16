using Microsoft.Extensions.Logging;

namespace BulkSharp.Gateway.Logging;

internal static partial class LogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshing operation registry from {Count} backends")]
    public static partial void RefreshingRegistry(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend '{Backend}' returned {StatusCode} for discovery")]
    public static partial void BackendDiscoveryFailed(this ILogger logger, string backend, System.Net.HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backend '{Backend}' registered {Count} operations")]
    public static partial void BackendOperationsRegistered(this ILogger logger, string backend, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Duplicate operation name detected during registry refresh")]
    public static partial void DuplicateOperationDetected(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh operations from backend '{Backend}'")]
    public static partial void BackendRefreshFailed(this ILogger logger, Exception ex, string backend);

    // ── GatewayAggregator ──────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch operations from backend '{Service}'")]
    public static partial void AggregateDiscoveryFailed(this ILogger logger, Exception ex, string service);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch operations list from backend '{Service}'")]
    public static partial void AggregateListFailed(this ILogger logger, Exception ex, string service);

    // ── GatewayRouter ──────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss for operation {OperationId}, fanning out to all backends")]
    public static partial void RouterCacheMiss(this ILogger logger, Guid operationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Backend '{Service}' failed during fan-out for {OperationId}")]
    public static partial void RouterFanOutFailed(this ILogger logger, Exception ex, string service, Guid operationId);
}
