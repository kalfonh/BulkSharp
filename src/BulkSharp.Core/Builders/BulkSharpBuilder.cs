using System.Reflection;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Abstractions.Export;
using BulkSharp.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Builders;

/// <summary>
/// Builder for configuring BulkSharp with clear separation of concerns
/// between file storage, metadata storage, and scheduling.
/// </summary>
public sealed class BulkSharpBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Assembly> _operationAssemblies = new();
    private bool _hasFileStorage;
    private bool _hasMetadataStorage;
    private bool _hasScheduler;

    internal BulkSharpBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Whether file storage has been configured.
    /// </summary>
    internal bool HasFileStorage => _hasFileStorage;

    /// <summary>
    /// Whether metadata storage has been configured.
    /// </summary>
    internal bool HasMetadataStorage => _hasMetadataStorage;

    /// <summary>
    /// Whether a scheduler has been configured.
    /// </summary>
    internal bool HasScheduler => _hasScheduler;

    /// <summary>
    /// Restrict operation discovery to specific assemblies.
    /// If not called, all loaded assemblies are scanned (default behavior).
    /// </summary>
    public BulkSharpBuilder AddOperationsFromAssembly(Assembly assembly)
    {
        _operationAssemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Restrict operation discovery to the assembly containing the specified type.
    /// </summary>
    public BulkSharpBuilder AddOperationsFromAssemblyOf<T>()
    {
        _operationAssemblies.Add(typeof(T).Assembly);
        return this;
    }

    /// <summary>
    /// Gets the assemblies configured for operation discovery, or null if all loaded assemblies should be scanned.
    /// </summary>
    internal IReadOnlyList<Assembly>? OperationAssemblies =>
        _operationAssemblies.Count > 0 ? _operationAssemblies : null;

    /// <summary>
    /// Configure file storage (where actual files are stored)
    /// </summary>
    public BulkSharpBuilder UseFileStorage(Action<FileStorageBuilder> configure)
    {
        if (_hasFileStorage)
            throw new InvalidOperationException("File storage has already been configured. UseFileStorage can only be called once.");

        var builder = new FileStorageBuilder(_services);
        configure(builder);
        _hasFileStorage = true;
        return this;
    }

    /// <summary>
    /// Configure metadata storage (where operation and file metadata is stored)
    /// </summary>
    public BulkSharpBuilder UseMetadataStorage(Action<MetadataStorageBuilder> configure)
    {
        if (_hasMetadataStorage)
            throw new InvalidOperationException("Metadata storage has already been configured. UseMetadataStorage can only be called once.");

        var builder = new MetadataStorageBuilder(_services);
        configure(builder);
        _hasMetadataStorage = true;
        return this;
    }

    /// <summary>
    /// Configure scheduler (how operations are queued and processed)
    /// </summary>
    public BulkSharpBuilder UseScheduler(Action<SchedulerBuilder> configure)
    {
        if (_hasScheduler)
            throw new InvalidOperationException("Scheduler has already been configured. UseScheduler can only be called once.");

        var builder = new SchedulerBuilder(_services);
        configure(builder);
        _hasScheduler = true;
        return this;
    }

    /// <summary>
    /// Registers a custom event handler that receives lifecycle events during bulk operation processing.
    /// </summary>
    public BulkSharpBuilder AddEventHandler<T>() where T : class, IBulkOperationEventHandler
    {
        _services.AddScoped<IBulkOperationEventHandler, T>();
        return this;
    }

    /// <summary>
    /// Configure global BulkSharp options.
    /// </summary>
    public BulkSharpBuilder ConfigureOptions(Action<BulkSharpOptions> configure)
    {
        _services.AddOptions<BulkSharpOptions>().Configure(configure);
        return this;
    }

    /// <summary>
    /// Registers a custom export formatter. If not called, the default CSV/JSON formatter is used.
    /// </summary>
    public BulkSharpBuilder UseExportFormatter<T>() where T : class, IBulkExportFormatter
    {
        _services.AddSingleton<IBulkExportFormatter, T>();
        return this;
    }
}
