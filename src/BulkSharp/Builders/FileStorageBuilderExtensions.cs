using BulkSharp.Builders;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Processing.Storage;
using BulkSharp.Processing.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp;

/// <summary>
/// Built-in file storage provider extension methods for FileStorageBuilder.
/// </summary>
public static class FileStorageBuilderExtensions
{
    /// <summary>
    /// Use local filesystem for file storage.
    /// </summary>
    public static FileStorageBuilder UseFileSystem(this FileStorageBuilder builder, Action<FileSystemStorageOptions>? configure = null)
    {
        builder.EnsureNotConfigured();
        var opts = new FileSystemStorageOptions();
        configure?.Invoke(opts);
        opts.Validate();
        var provider = new BasicFileStorageProvider(opts.BasePath);
        builder.Services.AddSingleton<IFileStorageProvider>(provider);
        return builder;
    }

    /// <summary>
    /// Use in-memory storage (for testing only).
    /// </summary>
    public static FileStorageBuilder UseInMemory(this FileStorageBuilder builder)
    {
        builder.EnsureNotConfigured();
        builder.Services.AddSingleton<InMemoryStorageProvider>();
        builder.Services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<InMemoryStorageProvider>());
        return builder;
    }
}
