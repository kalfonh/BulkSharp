using BulkSharp.Core.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Builders;

/// <summary>
/// Builder for configuring file storage options.
/// Custom providers must implement IFileStorageProvider.
/// </summary>
public sealed class FileStorageBuilder
{
    private readonly IServiceCollection _services;
    private bool _configured;

    /// <summary>
    /// The service collection for registering additional dependencies.
    /// Used by storage provider extension packages.
    /// </summary>
    public IServiceCollection Services => _services;

    internal FileStorageBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Marks this builder as configured. Called by extension methods to enforce single-configuration guard.
    /// </summary>
    public void EnsureNotConfigured()
    {
        if (_configured)
            throw new InvalidOperationException("A file storage provider has already been configured.");
        _configured = true;
    }

    /// <summary>
    /// Use a custom file storage provider (e.g., S3, Azure Blob).
    /// The provider must implement IFileStorageProvider.
    /// </summary>
    public FileStorageBuilder UseCustom<T>() where T : class, IFileStorageProvider
    {
        EnsureNotConfigured();
        _services.AddSingleton<T>();
        _services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<T>());
        return this;
    }

    /// <summary>
    /// Use a custom file storage registration with full control over service registration.
    /// </summary>
    public FileStorageBuilder UseCustom(Action<IServiceCollection> configure)
    {
        EnsureNotConfigured();
        configure(_services);
        return this;
    }
}
