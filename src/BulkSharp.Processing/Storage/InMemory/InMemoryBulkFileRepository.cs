namespace BulkSharp.Processing.Storage.InMemory;

internal sealed class InMemoryBulkFileRepository : IBulkFileRepository
{
    private readonly ConcurrentDictionary<Guid, BulkFile> _files = new();

    public Task<BulkFile> CreateAsync(BulkFile file, CancellationToken cancellationToken = default)
    {
        _files[file.Id] = file;
        return Task.FromResult(file);
    }

    public Task<BulkFile?> GetByIdAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        _files.TryGetValue(fileId, out var file);
        return Task.FromResult(file);
    }

    public Task UpdateAsync(BulkFile file, CancellationToken cancellationToken = default)
    {
        _files[file.Id] = file;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (file != null)
        {
            file.IsDeleted = true;
            file.DeletedAt = DateTime.UtcNow;
            await UpdateAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }
}
