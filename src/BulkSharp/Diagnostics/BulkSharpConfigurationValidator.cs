namespace BulkSharp.Diagnostics;

/// <summary>
/// Validates BulkSharp configuration at startup to catch misconfigurations early.
/// </summary>
internal static class BulkSharpConfigurationValidator
{
    /// <summary>
    /// Validates that all required BulkSharp services are registered.
    /// Call after <c>AddBulkSharp()</c> to catch misconfigurations early.
    /// </summary>
    /// <param name="services">The service collection to validate.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required services are missing.</exception>
    public static IServiceCollection ValidateBulkSharpConfiguration(this IServiceCollection services)
    {
        var requiredServices = new[]
        {
            typeof(IBulkOperationService),
            typeof(IBulkScheduler),
            typeof(IBulkOperationRepository),
            typeof(IBulkFileRepository),
            typeof(IFileStorageProvider),
            typeof(IBulkRowRecordRepository)
        };

        var missing = requiredServices
            .Where(t => !services.Any(s => s.ServiceType == t))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"BulkSharp configuration is incomplete. Missing services: {string.Join(", ", missing.Select(t => t.Name))}. " +
                "Ensure AddBulkSharp() or AddBulkSharpApi() has been called with proper configuration.");
        }

        // If a real scheduler is registered (not NullBulkScheduler), then processor must also be registered
        var schedulerDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IBulkScheduler));
        if (schedulerDescriptor != null)
        {
            var isNullScheduler = schedulerDescriptor.ImplementationType == typeof(NullBulkScheduler)
                || (schedulerDescriptor.ImplementationFactory != null
                    && !services.Any(s => s.ServiceType == typeof(IBulkOperationProcessor)));

            var hasProcessor = services.Any(s => s.ServiceType == typeof(IBulkOperationProcessor));

            if (!isNullScheduler && !hasProcessor)
            {
                throw new InvalidOperationException(
                    "BulkSharp configuration is incomplete. A scheduler is registered but IBulkOperationProcessor is missing. " +
                    "Use AddBulkSharp() for full worker registration, or AddBulkSharpApi() for API-only mode.");
            }
        }

        // Check if step-based operations are registered but IBulkRowRecordRepository is missing
        var hasRowRecordRepository = services.Any(s => s.ServiceType == typeof(IBulkRowRecordRepository));
        if (!hasRowRecordRepository)
        {
            var discoveryDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IBulkOperationDiscovery));
            if (discoveryDescriptor != null)
            {
                // Fall back to assembly scanning to detect step-based operations
                var assemblyScope = services.FirstOrDefault(s => s.ServiceType == typeof(BulkOperationAssemblyScope));
                var assemblies = assemblyScope?.ImplementationInstance is BulkOperationAssemblyScope scope
                    ? scope.Assemblies
                    : null;

                var hasStepBasedOps = BulkOperationDiscoveryService.ScanAssemblies(assemblies).Any(op => op.IsStepBased);
                if (hasStepBasedOps)
                {
                    throw new InvalidOperationException(
                        "Step-based operations are registered but IBulkRowRecordRepository is not configured. " +
                        "Register a metadata storage provider that includes row record support, " +
                        "such as UseInMemory() or UseEntityFramework().");
                }
            }
        }

        return services;
    }
}
