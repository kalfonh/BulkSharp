namespace BulkSharp.Processing.Storage.InMemory;

internal sealed class InMemoryStorageProvider : IFileStorageProvider
{
    private readonly ConcurrentDictionary<Guid, byte[]> _files = new();
    private readonly ConcurrentDictionary<Guid, (string FileName, DateTime CreatedAt)> _meta = new();

    public string ProviderName => StorageProviderNames.InMemory;

    public async Task<Guid> StoreFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        _files[fileId] = memoryStream.ToArray();
        _meta[fileId] = (fileName, DateTime.UtcNow);
        return fileId;
    }

    public Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        _files.TryRemove(fileId, out _);
        _meta.TryRemove(fileId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> FileExistsAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files.ContainsKey(fileId));

    public Task<BulkFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(fileId, out var data))
            return Task.FromResult<BulkFileMetadata?>(null);

        var meta = _meta.TryGetValue(fileId, out var m) ? m : (FileName: string.Empty, CreatedAt: DateTime.UtcNow);

        var fm = new BulkFileMetadata
        {
            Id = fileId,
            FileName = meta.FileName,
            Size = data.LongLength,
            CreatedAt = meta.CreatedAt
        };

        return Task.FromResult<BulkFileMetadata?>(fm);
    }

    public Task<IEnumerable<BulkFileMetadata>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        var result = _files.Keys.Select(id =>
        {
            var data = _files[id];
            var meta = _meta.TryGetValue(id, out var m) ? m : (FileName: string.Empty, CreatedAt: DateTime.UtcNow);
            return new BulkFileMetadata
            {
                Id = id,
                FileName = meta.FileName,
                Size = data.LongLength,
                CreatedAt = meta.CreatedAt
            };
        }).Where(fm => string.IsNullOrEmpty(prefix) || fm.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .ToList();

        return Task.FromResult<IEnumerable<BulkFileMetadata>>(result);
    }

    public Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (_files.TryGetValue(fileId, out var data))
        {
            return Task.FromResult<Stream>(new MemoryStream(data));
        }
        throw new FileNotFoundException($"File {fileId} not found");
    }
}
