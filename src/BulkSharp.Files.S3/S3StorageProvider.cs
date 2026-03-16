using Amazon.S3;
using Amazon.S3.Model;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Files;
using Microsoft.Extensions.Options;

namespace BulkSharp.Files.S3;

/// <summary>
/// Amazon S3 file storage provider for BulkSharp.
/// Stores files as "{prefix}{guid}-{sanitizedFileName}" in the configured bucket.
/// </summary>
internal sealed class S3StorageProvider(IAmazonS3 s3Client, IOptions<S3StorageOptions> options) : IFileStorageProvider
{
    private readonly S3StorageOptions _options = options.Value;

    /// <inheritdoc />
    public string ProviderName => "S3";

    /// <inheritdoc />
    public async Task<Guid> StoreFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var safeFileName = SanitizeFileName(fileName);
        var fileId = Guid.NewGuid();
        var key = BuildKey(fileId, safeFileName);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = DetectContentType(safeFileName)
        };

        await s3Client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        return fileId;
    }

    /// <inheritdoc />
    public async Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var key = await FindKeyAsync(fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File {fileId} not found in S3 bucket '{_options.BucketName}'.");

        var response = await s3Client.GetObjectAsync(_options.BucketName, key, cancellationToken).ConfigureAwait(false);
        return new S3ResponseStream(response);
    }

    /// <inheritdoc />
    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var key = await FindKeyAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (key is null) return;

        await s3Client.DeleteObjectAsync(_options.BucketName, key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BulkFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var key = await FindKeyAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (key is null) return null;

        try
        {
            var metadata = await s3Client.GetObjectMetadataAsync(_options.BucketName, key, cancellationToken).ConfigureAwait(false);
            return new BulkFileMetadata
            {
                Id = fileId,
                FileName = ExtractFileName(key),
                Size = metadata.ContentLength,
                ContentType = metadata.Headers.ContentType,
                CreatedAt = metadata.LastModified.ToUniversalTime(),
                ChecksumMD5 = metadata.ETag?.Trim('"')
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> FileExistsAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var key = await FindKeyAsync(fileId, cancellationToken).ConfigureAwait(false);
        return key is not null;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<BulkFileMetadata>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        var s3Prefix = string.IsNullOrEmpty(prefix) ? _options.Prefix : _options.Prefix + prefix;
        var request = new ListObjectsV2Request
        {
            BucketName = _options.BucketName,
            Prefix = s3Prefix
        };

        List<BulkFileMetadata> results = [];

        ListObjectsV2Response response;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            response = await s3Client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            foreach (var obj in response.S3Objects)
            {
                if (!TryParseFileId(obj.Key, out var fileId, out var fileName))
                    continue;

                results.Add(new BulkFileMetadata
                {
                    Id = fileId,
                    FileName = fileName,
                    Size = obj.Size,
                    CreatedAt = obj.LastModified.ToUniversalTime(),
                    ChecksumMD5 = obj.ETag?.Trim('"')
                });
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);

        return results;
    }

    private string BuildKey(Guid fileId, string sanitizedFileName) =>
        $"{_options.Prefix}{fileId}-{sanitizedFileName}";

    private async Task<string?> FindKeyAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var keyPrefix = $"{_options.Prefix}{fileId}-";
        var request = new ListObjectsV2Request
        {
            BucketName = _options.BucketName,
            Prefix = keyPrefix,
            MaxKeys = 1
        };

        var response = await s3Client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
        return response.S3Objects.Count > 0 ? response.S3Objects[0].Key : null;
    }

    private bool TryParseFileId(string key, out Guid fileId, out string fileName)
    {
        fileId = Guid.Empty;
        fileName = string.Empty;

        // Strip the configured prefix
        var relative = key;
        if (!string.IsNullOrEmpty(_options.Prefix))
        {
            if (!key.StartsWith(_options.Prefix, StringComparison.Ordinal))
                return false;
            relative = key[_options.Prefix.Length..];
        }

        // Expected format: "{guid}-{originalName}" where guid is 36 chars
        if (relative.Length <= 37 || relative[36] != '-')
            return false;

        if (!Guid.TryParse(relative[..36], out fileId))
            return false;

        fileName = relative[37..];
        return true;
    }

    private string ExtractFileName(string key)
    {
        return TryParseFileId(key, out _, out var fileName) ? fileName : key;
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
            throw new ArgumentException("Invalid file name.", nameof(fileName));
        return safe;
    }

    private static string DetectContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Wraps the S3 response stream and ensures the <see cref="GetObjectResponse"/>
    /// is disposed when the stream is closed, preventing HTTP connection pool exhaustion.
    /// </summary>
    private sealed class S3ResponseStream : Stream
    {
        private readonly GetObjectResponse _response;
        private readonly Stream _inner;

        public S3ResponseStream(GetObjectResponse response)
        {
            _response = response;
            _inner = response.ResponseStream;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            _inner.CopyToAsync(destination, bufferSize, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
