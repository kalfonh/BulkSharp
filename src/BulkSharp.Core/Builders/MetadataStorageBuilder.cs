using BulkSharp.Core.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Builders;

/// <summary>
/// Builder for configuring metadata storage options
/// </summary>
public sealed class MetadataStorageBuilder
{
    private readonly IServiceCollection _services;
    private bool _configured;

    /// <summary>
    /// The service collection for registering additional dependencies.
    /// Used by metadata storage extension packages.
    /// </summary>
    public IServiceCollection Services => _services;

    internal MetadataStorageBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Marks this builder as configured. Called by extension methods to enforce single-configuration guard.
    /// </summary>
    public void EnsureNotConfigured()
    {
        if (_configured)
            throw new InvalidOperationException("A metadata storage provider has already been configured.");
        _configured = true;
    }

    /// <summary>
    /// Use a custom metadata storage registration.
    /// External packages like BulkSharp.Data.EntityFramework use this to register their implementations.
    /// </summary>
    /// <example>
    /// builder.UseMetadataStorage(ms => ms.UseCustom(s => s.AddBulkSharpSqlServer(opts => opts.ConnectionString = "...")));
    /// </example>
    public MetadataStorageBuilder UseCustom(Action<IServiceCollection> configure)
    {
        EnsureNotConfigured();
        configure(_services);
        return this;
    }
}
