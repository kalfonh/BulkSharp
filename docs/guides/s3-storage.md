# S3 Storage

BulkSharp provides Amazon S3 file storage via the `BulkSharp.Files.S3` package.

## Installation

```bash
dotnet add package BulkSharp.Files.S3
```

## Configuration

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseS3(opts =>
    {
        opts.BucketName = "my-bulksharp-uploads";
        opts.Region = "us-east-1";
        opts.KeyPrefix = "uploads/";           // Optional: prefix for S3 keys
    })));
```

## AWS Authentication

The S3 provider uses the AWS SDK's default credential chain. Configure credentials via:
- Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
- AWS credentials file (`~/.aws/credentials`)
- IAM role (EC2, ECS, Lambda)
- Any other AWS SDK credential provider

## IAM Permissions

The S3 provider requires these permissions on the configured bucket:

```json
{
  "Effect": "Allow",
  "Action": [
    "s3:PutObject",
    "s3:GetObject",
    "s3:DeleteObject"
  ],
  "Resource": "arn:aws:s3:::my-bulksharp-uploads/*"
}
```

## Local Development with LocalStack

For local development without AWS, use [LocalStack](https://localstack.cloud/):

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseS3(opts =>
    {
        opts.BucketName = "local-uploads";
        opts.ServiceUrl = "http://localhost:4566";  // LocalStack endpoint
        opts.ForcePathStyle = true;
    })));
```

The Production sample (`samples/BulkSharp.Sample.Production`) includes a working LocalStack setup via Aspire.
