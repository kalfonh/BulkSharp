namespace BulkSharp.Core.Abstractions.Storage;

/// <summary>
/// High-level storage provider that combines file storage with metadata persistence.
/// Operates on application-level file IDs (BulkFile.Id), not raw storage keys.
/// Internally composes an IFileStorageProvider for physical file I/O.
/// </summary>
public interface IManagedStorageProvider
{
    Task<BulkFile> StoreFileAsync(Stream fileStream, string fileName, string uploadedBy, CancellationToken cancellationToken = default);
    Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<BulkFile?> GetFileInfoAsync(Guid fileId, CancellationToken cancellationToken = default);
}
