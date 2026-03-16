using BulkSharp;
using BulkSharp.Builders;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Abstractions.Export;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Configuration;
using BulkSharp.Processing.Export;
using BulkSharp.Processing.Scheduling;
using BulkSharp.Processing.Services;
using BulkSharp.Processing.Storage.InMemory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring BulkSharp services.
/// This is the main entry point for the BulkSharp library.
/// </summary>
public static class BulkSharpServiceCollectionExtensions
{
    /// <summary>
    /// Adds BulkSharp services with clear separation of concerns.
    /// Configure file storage, metadata storage, and scheduling independently.
    /// </summary>
    public static IServiceCollection AddBulkSharp(
        this IServiceCollection services,
        Action<BulkSharpBuilder> configure)
    {
        // Guard against double registration
        if (services.Any(s => s.ServiceType == typeof(IBulkOperationProcessor)))
        {
            return services;
        }

        var builder = new BulkSharpBuilder(services);
        configure(builder);

        // Apply defaults if not configured
        if (!builder.HasFileStorage)
            builder.UseFileStorage(fs => fs.UseFileSystem());

        if (!builder.HasMetadataStorage)
            builder.UseMetadataStorage(ms => ms.UseInMemory());

        if (!builder.HasScheduler)
            builder.UseScheduler(s => s.UseChannels());

        // Default ServiceName if not explicitly configured
        services.AddOptions<BulkSharpOptions>()
            .PostConfigure(options =>
            {
                if (string.IsNullOrEmpty(options.ServiceName))
                    options.ServiceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "default";
            });

        // Register options with validation
        services.AddOptions<BulkSharpOptions>()
            .PostConfigure(options => options.Validate())
            .ValidateOnStart();

        // Register bulk operations from discovered assemblies
        services.RegisterBulkOperations(builder.OperationAssemblies);

        // Register core processing services
        services.RegisterProcessingServices();

        return services;
    }

    /// <summary>
    /// Adds BulkSharp with default configuration.
    /// Applies sensible defaults for all components.
    /// </summary>
    public static IServiceCollection AddBulkSharp(this IServiceCollection services) =>
        services.AddBulkSharp(_ => { });

    /// <summary>
    /// Adds BulkSharp with in-memory storage and immediate scheduler.
    /// Suitable for testing and development.
    /// </summary>
    public static IServiceCollection AddBulkSharpInMemory(this IServiceCollection services) =>
        services.AddBulkSharp(builder => builder
                    .UseFileStorage(fs => fs.UseInMemory())
                    .UseMetadataStorage(ms => ms.UseInMemory())
                    .UseScheduler(s => s.UseImmediate()));

    /// <summary>
    /// Adds BulkSharp with file system storage and Channels scheduler.
    /// Suitable for production use with in-memory metadata.
    /// </summary>
    public static IServiceCollection AddBulkSharpDefaults(this IServiceCollection services) =>
        services.AddBulkSharp(builder => builder
                    .UseFileStorage(fs => fs.UseFileSystem())
                    .UseMetadataStorage(ms => ms.UseInMemory())
                    .UseScheduler(s => s.UseChannels()));

    /// <summary>
    /// Adds BulkSharp API services only — no worker infrastructure.
    /// Registers operation service, query service, file/metadata storage, and data format processors.
    /// Uses <see cref="NullBulkScheduler"/> so operations stay in Pending status for a separate Worker to pick up.
    /// </summary>
    public static IServiceCollection AddBulkSharpApi(
        this IServiceCollection services,
        Action<BulkSharpBuilder> configure)
    {
        // Guard against double registration (check both API and full registration markers)
        if (services.Any(s => s.ServiceType == typeof(IBulkOperationProcessor))
            || services.Any(s => s.ServiceType == typeof(NullBulkSchedulerMarker)))
        {
            return services;
        }

        var builder = new BulkSharpBuilder(services);
        configure(builder);

        // Apply defaults for storage only — no scheduler default
        if (!builder.HasFileStorage)
            builder.UseFileStorage(fs => fs.UseFileSystem());

        if (!builder.HasMetadataStorage)
            builder.UseMetadataStorage(ms => ms.UseInMemory());

        // Default ServiceName if not explicitly configured
        services.AddOptions<BulkSharpOptions>()
            .PostConfigure(options =>
            {
                if (string.IsNullOrEmpty(options.ServiceName))
                    options.ServiceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "default";
            });

        // Register options with validation
        services.AddOptions<BulkSharpOptions>()
            .PostConfigure(options => options.Validate())
            .ValidateOnStart();

        // Register bulk operations from discovered assemblies (needed for discovery/validation)
        services.RegisterBulkOperations(builder.OperationAssemblies);

        // Register API-only services (no processors, step executors, or hosted services)
        services.RegisterApiServices();

        return services;
    }

    /// <summary>
    /// Adds BulkSharp API services with default configuration.
    /// </summary>
    public static IServiceCollection AddBulkSharpApi(this IServiceCollection services) =>
        services.AddBulkSharpApi(_ => { });

    internal static IServiceCollection RegisterBulkOperations(
        this IServiceCollection services,
        IEnumerable<Assembly>? assemblies = null)
    {
        if (assemblies != null)
        {
            var assemblyList = assemblies.ToList();
            services.TryAddSingleton(new BulkOperationAssemblyScope(assemblyList));
        }

        services.TryAddSingleton<IBulkOperationDiscovery, BulkOperationDiscoveryService>();

        foreach (var op in BulkOperationDiscoveryService.ScanAssemblies(assemblies))
            services.TryAddScoped(op.OperationType);

        // Auto-discover and register validators, processors, and event handlers
        // from the same assemblies used for operation discovery.
        services.RegisterDiscoveredExtensions(assemblies);

        return services;
    }

    private static readonly HashSet<Type> ExtensionInterfaces = typeof(IBulkMetadata).Assembly.GetTypes()
        .Where(t => t.IsInterface && t.GetCustomAttribute<BulkExtensionPointAttribute>() != null)
        .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)
        .ToHashSet();

    internal static void RegisterDiscoveredExtensions(
        this IServiceCollection services,
        IEnumerable<Assembly>? assemblies = null)
    {
        var targetAssemblies = assemblies
            ?? BulkOperationDiscoveryService.ScanAssemblies()
                .Select(op => op.OperationType.Assembly)
                .Distinct();

        foreach (var assembly in targetAssemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    var match = iface.IsGenericType
                        ? ExtensionInterfaces.Contains(iface.GetGenericTypeDefinition())
                        : ExtensionInterfaces.Contains(iface);

                    if (!match) continue;

                    if (services.Any(d => d.ServiceType == iface && d.ImplementationType == type))
                        continue;

                    services.AddScoped(iface, type);
                }
            }
        }
    }

    internal static void RegisterApiServices(this IServiceCollection services)
    {
        // Marker to prevent double registration
        services.TryAddSingleton<NullBulkSchedulerMarker>();

        // Signal service (needed for signal endpoints even in API mode)
        services.TryAddSingleton<BulkStepSignalService>();
        services.TryAddSingleton<IBulkStepSignalService>(sp => sp.GetRequiredService<BulkStepSignalService>());
        services.TryAddSingleton<IBulkStepSignalRegistry>(sp => sp.GetRequiredService<BulkStepSignalService>());

        // Core operation services
        services.AddScoped<IBulkOperationService, BulkOperationService>();
        services.AddScoped<IBulkOperationQueryService>(sp => sp.GetRequiredService<IBulkOperationService>());

        // Managed storage provider
        services.AddScoped<IManagedStorageProvider, ManagedStorageProvider>();

        // Data format processors (needed for validation)
        services.AddScoped(typeof(IDataFormatProcessorFactory<>), typeof(DataFormatProcessorFactory<>));
        services.AddScoped(typeof(IDataFormatProcessor<>), typeof(CsvDataFormatProcessor<>));
        services.AddScoped(typeof(IDataFormatProcessor<>), typeof(JsonDataFormatProcessor<>));

        // Event dispatcher
        services.TryAddScoped<IBulkOperationEventDispatcher, BulkOperationEventDispatcher>();

        // Retry and export services
        services.AddScoped<IBulkRetryService, BulkRetryService>();
        services.AddScoped<IBulkExportService, BulkExportService>();
        services.TryAddSingleton<IBulkExportFormatter, DefaultBulkExportFormatter>();
        services.TryAddSingleton<IBulkRowRetryHistoryRepository, InMemoryBulkRowRetryHistoryRepository>();

        // Null scheduler — operations stay Pending for external Worker
        services.TryAddSingleton<IBulkScheduler, NullBulkScheduler>();
    }

    internal static void RegisterProcessingServices(this IServiceCollection services)
    {
        services.TryAddSingleton<BulkStepSignalService>();
        services.TryAddSingleton<IBulkStepSignalService>(sp => sp.GetRequiredService<BulkStepSignalService>());
        services.TryAddSingleton<IBulkStepSignalRegistry>(sp => sp.GetRequiredService<BulkStepSignalService>());
        services.AddScoped<IBulkOperationService, BulkOperationService>();
        services.AddScoped<IBulkOperationQueryService>(sp => sp.GetRequiredService<IBulkOperationService>());
        services.AddScoped<IAsyncStepCompletionHandler, PollingCompletionHandler>();
        services.AddScoped<IAsyncStepCompletionHandler, SignalCompletionHandler>();
        services.AddScoped<IRowRecordPersistenceProvider, RowRecordPersistenceProvider>();
        services.AddScoped<IBulkStepExecutor, BulkStepExecutorService>();
        services.AddScoped<IBulkStepRecordManager, BulkStepRecordManager>();
        services.AddScoped<IBulkOperationProcessor, BulkOperationProcessor>();
        services.AddScoped<IRowRecordFlushService, RowRecordFlushService>();
        services.AddScoped<IRowExecutionStrategy>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<BulkSharpOptions>>().Value;
            return opts.MaxRowConcurrency == 1
                ? ActivatorUtilities.CreateInstance<SequentialRowExecutionStrategy>(sp)
                : ActivatorUtilities.CreateInstance<ParallelRowExecutionStrategy>(sp);
        });
        services.AddScoped(typeof(ITypedBulkOperationProcessor<,,>), typeof(TypedBulkOperationProcessor<,,>));
        services.AddScoped(typeof(IRowValidationPipeline<,>), typeof(RowValidationPipeline<,>));
        services.AddScoped(typeof(IDataFormatProcessorFactory<>), typeof(DataFormatProcessorFactory<>));
        services.AddScoped(typeof(IDataFormatProcessor<>), typeof(CsvDataFormatProcessor<>));
        services.AddScoped(typeof(IDataFormatProcessor<>), typeof(JsonDataFormatProcessor<>));

        // Register managed storage provider that combines file storage with metadata
        services.AddScoped<IManagedStorageProvider, ManagedStorageProvider>();

        // Event dispatcher
        services.TryAddScoped<IBulkOperationEventDispatcher, BulkOperationEventDispatcher>();

        // Retry and export services
        services.AddScoped<IBulkRetryService, BulkRetryService>();
        services.AddScoped<IBulkExportService, BulkExportService>();
        services.TryAddSingleton<IBulkExportFormatter, DefaultBulkExportFormatter>();
        services.TryAddSingleton<IBulkRowRetryHistoryRepository, InMemoryBulkRowRetryHistoryRepository>();

        // Register orphaned step recovery as a hosted service.
        // When EnableOrphanedStepRecovery is true, it runs on startup to
        // transition rows stuck in WaitingForCompletion after a restart.
        services.AddHostedService<OrphanedStepRecoveryService>();
    }
}

/// <summary>
/// Marker type used to detect AddBulkSharpApi double registration.
/// </summary>
internal sealed class NullBulkSchedulerMarker;
