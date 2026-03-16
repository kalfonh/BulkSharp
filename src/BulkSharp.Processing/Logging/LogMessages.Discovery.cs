namespace BulkSharp.Processing.Logging;

internal static partial class LogMessages
{
    // ── BulkOperationDiscoveryService ──────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, Message = "No assemblies specified for BulkSharp operation scanning — falling back to AppDomain.CurrentDomain.GetAssemblies() which is non-deterministic. Use AddOperationsFromAssemblyOf<T>() for reliable discovery.")]
    public static partial void AssemblyScanningFallback(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Type {TypeName} has [BulkOperation] attribute but does not implement IBulkOperationBase<,>. It will be skipped.")]
    public static partial void OperationTypeWithoutInterface(this ILogger logger, string? typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discovered bulk operation: {OperationName} ({Type})")]
    public static partial void DiscoveredBulkOperation(this ILogger logger, string operationName, string? type);
}
