namespace BulkSharp.Core.Abstractions.Storage;

/// <summary>
/// File storage provider abstraction. Stores, retrieves, and manages files by generated Guid key.
/// Implement this for custom storage backends (S3, Azure Blob, etc.).
/// </summary>
public interface IFileStorageProvider
{
    /// <summary>Provider display name (e.g. "FileSystem", "InMemory", "S3").</summary>
    string ProviderName { get; }

    /// <summary>Stores a file and returns the generated storage key.</summary>
    Task<Guid> StoreFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a file stream by its storage key.</summary>
    Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a file by its storage key.</summary>
    Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Returns metadata for a stored file, or null if not found.</summary>
    Task<BulkFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a file exists in storage.</summary>
    Task<bool> FileExistsAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Lists files in storage, optionally filtered by name prefix.</summary>
    Task<IEnumerable<BulkFileMetadata>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default);
}
