using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using BulkSharp.Core.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BulkSharp.Files.S3;

/// <summary>
/// Extension methods for registering S3 file storage with BulkSharp.
/// </summary>
public static class S3FileStorageExtensions
{
    /// <summary>
    /// Registers Amazon S3 as the file storage provider for BulkSharp.
    /// </summary>
    public static IServiceCollection AddBulkSharpS3Storage(
        this IServiceCollection services,
        Action<S3StorageOptions> configure)
    {
        services.AddOptions<S3StorageOptions>()
            .Configure(configure)
            .PostConfigure(o => o.Validate())
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;

            var config = new AmazonS3Config
            {
                ForcePathStyle = opts.ForcePathStyle
            };

            if (!string.IsNullOrEmpty(opts.ServiceUrl))
            {
                config.ServiceURL = opts.ServiceUrl;
                // When using a custom endpoint (LocalStack, MinIO), use dummy credentials
                // so the SDK doesn't try the default credential chain (EC2 IMDS, etc.)
                return new AmazonS3Client(
                    new BasicAWSCredentials("test", "test"),
                    config);
            }

            config.RegionEndpoint = RegionEndpoint.GetBySystemName(opts.Region);
            return new AmazonS3Client(config);
        });

        services.AddSingleton<S3StorageProvider>();
        services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<S3StorageProvider>());

        return services;
    }
}
