using BulkSharp.Builders;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Processing.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BulkSharp;

/// <summary>
/// Built-in metadata storage provider extension methods for MetadataStorageBuilder.
/// </summary>
public static class MetadataStorageBuilderExtensions
{
    /// <summary>
    /// Use in-memory storage (for testing only).
    /// </summary>
    public static MetadataStorageBuilder UseInMemory(this MetadataStorageBuilder builder)
    {
        builder.EnsureNotConfigured();
        builder.Services.AddSingleton<IBulkOperationRepository, InMemoryBulkOperationRepository>();
        builder.Services.AddSingleton<IBulkFileRepository, InMemoryBulkFileRepository>();
        builder.Services.TryAddSingleton<IBulkRowRecordRepository, InMemoryBulkRowRecordRepository>();
        return builder;
    }
}
