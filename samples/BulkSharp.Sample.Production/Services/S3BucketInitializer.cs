using Amazon.S3;
using Amazon.S3.Model;

namespace BulkSharp.Sample.Production.Services;

public sealed class S3BucketInitializer(
    IAmazonS3 s3Client,
    IConfiguration configuration,
    ILogger<S3BucketInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bucketName = configuration["S3:BucketName"] ?? "bulksharp-files";

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                var buckets = await s3Client.ListBucketsAsync(stoppingToken).ConfigureAwait(false);
                if (buckets.Buckets.Any(b => b.BucketName == bucketName))
                {
                    logger.LogInformation("S3 bucket '{BucketName}' already exists", bucketName);
                    return;
                }

                await s3Client.PutBucketAsync(new PutBucketRequest
                {
                    BucketName = bucketName
                }, stoppingToken).ConfigureAwait(false);

                logger.LogInformation("Created S3 bucket '{BucketName}'", bucketName);
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                logger.LogWarning(ex, "S3 not ready yet (attempt {Attempt}/30), retrying in 2s...", attempt);
                await Task.Delay(2000, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
