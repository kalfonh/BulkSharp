namespace BulkSharp.Files.S3;

/// <summary>
/// Configuration options for S3 file storage.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>S3 bucket name. Required.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Optional key prefix for all stored files (e.g. "bulksharp/").</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Custom service URL for S3-compatible endpoints (e.g. LocalStack "http://localhost:4566").
    /// When null, uses default AWS endpoint for the region.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Use path-style addressing (required for LocalStack and some S3-compatible stores).
    /// Default: false (virtual-hosted style).
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>AWS region. Default: us-east-1.</summary>
    public string Region { get; set; } = "us-east-1";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BucketName))
            throw new ArgumentException("BucketName is required.", nameof(BucketName));
    }
}
