namespace BulkSharp.Processing.Storage;

/// <summary>
/// File system storage provider. Stores files as "{guid}-{filename}" in a configurable directory.
/// Implements IFileStorageProvider for store/retrieve/delete and metadata/listing operations.
/// </summary>
internal sealed class BasicFileStorageProvider : IFileStorageProvider
{
    private readonly string _basePath;

    public string ProviderName => StorageProviderNames.FileSystem;

    public BasicFileStorageProvider(string basePath = "bulksharp-storage")
    {
        _basePath = Path.GetFullPath(basePath);
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
    }

    public async Task<Guid> StoreFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new ArgumentException("Invalid file name", nameof(fileName));

        var fileId = Guid.NewGuid();
        var filePath = Path.Combine(_basePath, $"{fileId}-{safeFileName}");
        var fullFilePath = Path.GetFullPath(filePath);
        if (!fullFilePath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Resolved path is outside the base directory");

        using var fileStreamOut = File.Create(filePath);
        await fileStream.CopyToAsync(fileStreamOut, cancellationToken).ConfigureAwait(false);

        return fileId;
    }

    public Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var files = FindFiles(fileId);
        if (files.Length == 0)
            throw new FileNotFoundException($"File {fileId} not found");

        ValidatePathSecurity(files[0]);
        Stream stream = new FileStream(files[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var files = FindFiles(fileId);
        foreach (var file in files)
        {
            ValidatePathSecurity(file);
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    public Task<bool> FileExistsAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(FindFiles(fileId).Length > 0);
    }

    public Task<BulkFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var files = FindFiles(fileId);
        if (files.Length == 0)
            return Task.FromResult<BulkFileMetadata?>(null);

        var fi = new FileInfo(files[0]);
        return Task.FromResult<BulkFileMetadata?>(new BulkFileMetadata
        {
            Id = fileId,
            FileName = ExtractFileName(files[0]),
            Size = fi.Length,
            CreatedAt = fi.CreationTimeUtc
        });
    }

    public Task<IEnumerable<BulkFileMetadata>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var allFiles = Directory.GetFiles(_basePath);
        var result = new List<BulkFileMetadata>();

        foreach (var file in allFiles)
        {
            var name = Path.GetFileName(file);
            // File format: "{guid}-{originalName}" where guid is 36 chars
            if (name.Length <= 37 || name[36] != '-') continue;
            if (!Guid.TryParse(name[..36], out var id)) continue;

            var fileName = name[37..];
            if (!string.IsNullOrEmpty(prefix) && !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var fi = new FileInfo(file);
            result.Add(new BulkFileMetadata
            {
                Id = id,
                FileName = fileName,
                Size = fi.Length,
                CreatedAt = fi.CreationTimeUtc
            });
        }

        return Task.FromResult<IEnumerable<BulkFileMetadata>>(result);
    }

    private string[] FindFiles(Guid fileId)
    {
        if (!Directory.Exists(_basePath))
            return Array.Empty<string>();

        return Directory.GetFiles(_basePath, $"{fileId}-*");
    }

    private void ValidatePathSecurity(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Access to path outside base directory is not allowed");
    }

    private static string ExtractFileName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        // File format is "{guid}-{originalName}" where guid is 36 chars (with dashes)
        // Skip the guid prefix and the separator dash
        if (name.Length > 37 && Guid.TryParse(name[..36], out _) && name[36] == '-')
            return name[37..];
        return name;
    }
}
