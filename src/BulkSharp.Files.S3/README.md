# BulkSharp.Files.S3

Amazon S3 file storage provider for BulkSharp bulk data processing.

## Features

- S3 and S3-compatible storage (LocalStack, MinIO)
- Configurable bucket, prefix, and region
- Proper HTTP connection disposal for connection pool safety

## Usage

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseS3(opts =>
    {
        opts.BucketName = "my-bucket";
        opts.Region = "us-east-1";
    })));
```

For LocalStack or MinIO:

```csharp
opts.ServiceUrl = "http://localhost:4566";
opts.ForcePathStyle = true;
```

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [S3 Storage Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/s3-storage.md)
