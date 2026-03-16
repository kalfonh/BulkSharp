using BulkSharp.Builders;

namespace BulkSharp.Files.S3;

/// <summary>
/// Extension methods for configuring S3 file storage on BulkSharp's FileStorageBuilder.
/// </summary>
public static class FileStorageBuilderExtensions
{
    /// <summary>
    /// Use Amazon S3 (or S3-compatible store like LocalStack/MinIO) for BulkSharp file storage.
    /// </summary>
    public static FileStorageBuilder UseS3(
        this FileStorageBuilder builder,
        Action<S3StorageOptions> configure)
    {
        builder.EnsureNotConfigured();
        builder.Services.AddBulkSharpS3Storage(configure);
        return builder;
    }
}
