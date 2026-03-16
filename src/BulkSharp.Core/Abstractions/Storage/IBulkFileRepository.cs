namespace BulkSharp.Core.Abstractions.Storage;

/// <summary>Persistence for file metadata records.</summary>
public interface IBulkFileRepository
{
    Task<BulkFile> CreateAsync(BulkFile file, CancellationToken cancellationToken = default);
    Task<BulkFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateAsync(BulkFile file, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
