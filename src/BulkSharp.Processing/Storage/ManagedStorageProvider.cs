namespace BulkSharp.Processing.Storage;

internal sealed class ManagedStorageProvider(
    IFileStorageProvider storageProvider,
    IBulkFileRepository fileRepository,
    IOptions<BulkSharpOptions> options) : IManagedStorageProvider
{
    private readonly IFileStorageProvider _storageProvider = storageProvider;
    private readonly IBulkFileRepository _fileRepository = fileRepository;
    private readonly BulkSharpOptions _options = options.Value;

    public async Task<BulkFile> StoreFileAsync(
        Stream fileStream,
        string fileName,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        if (_options.MaxFileSizeBytes > 0 && fileStream.CanSeek && fileStream.Length > _options.MaxFileSizeBytes)
        {
            throw new ArgumentException(
                $"File size ({fileStream.Length} bytes) exceeds maximum allowed size ({_options.MaxFileSizeBytes} bytes).");
        }

        var contentType = GetContentType(fileName);

        // Capture length before storage — the provider may close the stream
        var knownLength = fileStream.CanSeek ? fileStream.Length : 0L;

        // Wrap non-seekable streams to capture actual byte count after storage
        var countingStream = fileStream.CanSeek ? null : new ByteCountingStream(fileStream);
        var streamToStore = countingStream ?? fileStream;

        var storageKey = await _storageProvider.StoreFileAsync(streamToStore, fileName, cancellationToken).ConfigureAwait(false);

        var sizeBytes = countingStream?.BytesRead ?? knownLength;

        var bulkFile = new BulkFile
        {
            OriginalFileName = fileName,
            StorageKey = storageKey,
            StorageProvider = _storageProvider.ProviderName,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            UploadedBy = uploadedBy
        };

        return await _fileRepository.CreateAsync(bulkFile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _fileRepository.GetByIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (file == null || file.IsDeleted)
            throw new FileNotFoundException($"File {fileId} not found");

        return await _storageProvider.RetrieveFileAsync(file.StorageKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _fileRepository.GetByIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (file != null && !file.IsDeleted)
        {
            await _storageProvider.DeleteFileAsync(file.StorageKey, cancellationToken).ConfigureAwait(false);
            await _fileRepository.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<BulkFile?> GetFileInfoAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        _fileRepository.GetByIdAsync(fileId, cancellationToken);

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Read-only stream wrapper that counts the total bytes read from the inner stream.
    /// Used for non-seekable streams where Length/Position are not available.
    /// </summary>
    private sealed class ByteCountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = inner.Read(buffer, offset, count);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = inner.Read(buffer);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            var buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = await ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            }
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
