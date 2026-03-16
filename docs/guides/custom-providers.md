# Custom Providers

BulkSharp's storage and scheduling layers are fully pluggable. This guide covers how to implement and register custom providers for file storage, metadata storage, and scheduling. It also explains how to package a provider as a reusable NuGet extension.

## Table of Contents

- [Custom File Storage Provider](#custom-file-storage-provider)
- [Custom Metadata Storage](#custom-metadata-storage)
- [Custom Scheduler](#custom-scheduler)
- [Extension Package Pattern](#extension-package-pattern)

---

## Custom File Storage Provider

Implement `IFileStorageProvider` to store uploaded files in any backend.

### Interface Contract

```csharp
public interface IFileStorageProvider
{
    string ProviderName { get; }
    Task<Guid> StoreFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<BulkFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BulkFileMetadata>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default);
}
```

### Implementation Example: Azure Blob Storage

```csharp
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Files;

public sealed class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "bulksharp";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString is required.", nameof(ConnectionString));
        if (string.IsNullOrWhiteSpace(ContainerName))
            throw new ArgumentException("ContainerName is required.", nameof(ContainerName));
    }
}

public sealed class AzureBlobStorageProvider : IFileStorageProvider
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorageProvider(BlobContainerClient container)
    {
        _container = container;
    }

    public string ProviderName => "AzureBlob";

    public async Task<Guid> StoreFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        var blobName = $"{fileId}/{Path.GetFileName(fileName)}";
        var blob = _container.GetBlobClient(blobName);

        await blob.UploadAsync(fileStream, overwrite: true, cancellationToken).ConfigureAwait(false);
        return fileId;
    }

    public async Task<Stream> RetrieveFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var blobName = await FindBlobNameAsync(fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File {fileId} not found in container '{_container.Name}'.");

        var blob = _container.GetBlobClient(blobName);
        var response = await blob.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var blobName = await FindBlobNameAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (blobName is null) return;

        await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var blobName = await FindBlobNameAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (blobName is null) return null;

        var blob = _container.GetBlobClient(blobName);
        var props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new BulkFileMetadata
        {
            Id = fileId,
            FileName = blobName[(blobName.IndexOf('/') + 1)..],
            Size = props.Value.ContentLength,
            ContentType = props.Value.ContentType,
            CreatedAt = props.Value.CreatedOn.UtcDateTime,
            ChecksumMD5 = props.Value.ContentHash is { } hash ? Convert.ToBase64String(hash) : null
        };
    }

    public async Task<bool> FileExistsAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        return await FindBlobNameAsync(fileId, cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<IEnumerable<BulkFileMetadata>> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        var results = new List<BulkFileMetadata>();
        await foreach (var item in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            if (!TryParseFileId(item.Name, out var fileId, out var fileName))
                continue;

            results.Add(new BulkFileMetadata
            {
                Id = fileId,
                FileName = fileName,
                Size = item.Properties.ContentLength ?? 0,
                CreatedAt = item.Properties.CreatedOn?.UtcDateTime ?? DateTime.UtcNow,
                ContentType = item.Properties.ContentType
            });
        }
        return results;
    }

    private async Task<string?> FindBlobNameAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var prefix = $"{fileId}/";
        await foreach (var blob in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            return blob.Name;
        }
        return null;
    }

    private static bool TryParseFileId(string blobName, out Guid fileId, out string fileName)
    {
        fileId = Guid.Empty;
        fileName = string.Empty;

        var slashIndex = blobName.IndexOf('/');
        if (slashIndex < 0 || slashIndex + 1 >= blobName.Length) return false;
        if (!Guid.TryParse(blobName[..slashIndex], out fileId)) return false;

        fileName = blobName[(slashIndex + 1)..];
        return true;
    }
}
```

### Registration via UseCustom&lt;T&gt;()

For a quick inline registration without creating a separate NuGet package:

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseCustom<AzureBlobStorageProvider>())
    .UseMetadataStorage(ms => ms.UseInMemory())
    .UseScheduler(s => s.UseChannels()));
```

When using `UseCustom<T>()`, the builder registers your type as a singleton and wires it to `IFileStorageProvider`. If your provider requires constructor dependencies (like `BlobContainerClient`), register them before calling `AddBulkSharp`:

```csharp
services.AddSingleton(_ =>
{
    var client = new BlobServiceClient("UseDevelopmentStorage=true");
    var container = client.GetBlobContainerClient("bulksharp");
    container.CreateIfNotExists();
    return container;
});

services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseCustom<AzureBlobStorageProvider>()));
```

### Registration via FileStorageBuilder.Services

For more control, use the `Services` property directly. This is the pattern used by extension packages like `BulkSharp.Files.S3`:

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs =>
    {
        fs.EnsureNotConfigured();
        fs.Services.AddSingleton<BlobContainerClient>(_ =>
        {
            var client = new BlobServiceClient(connectionString);
            return client.GetBlobContainerClient("bulksharp");
        });
        fs.Services.AddSingleton<AzureBlobStorageProvider>();
        fs.Services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<AzureBlobStorageProvider>());
    }));
```

**Important:** When registering manually through `Services`, you must call `EnsureNotConfigured()` yourself to prevent double-configuration.

---

## Custom Metadata Storage

Metadata storage requires implementing four repository interfaces. All four must be registered for BulkSharp to function correctly.

### Required Interfaces

| Interface | Purpose |
|---|---|
| `IBulkOperationRepository` | CRUD + query for operation records |
| `IBulkRowRecordRepository` | Unified per-row tracking: validation, steps, errors, async completion |
| `IBulkFileRepository` | File metadata record persistence |

### Implementation Example: DynamoDB

```csharp
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain;
using BulkSharp.Core.Domain.Queries;

public sealed class DynamoOperationRepository : IBulkOperationRepository
{
    // Inject your DynamoDB client / table reference in the constructor.

    public async Task<BulkOperation> CreateAsync(BulkOperation bulkOperation, CancellationToken cancellationToken = default)
    {
        // Serialize and put item into DynamoDB table.
        // Return the persisted entity.
        throw new NotImplementedException();
    }

    public async Task<BulkOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Query by partition key (operation ID).
        throw new NotImplementedException();
    }

    public async Task<BulkOperation> UpdateAsync(BulkOperation bulkOperation, CancellationToken cancellationToken = default)
    {
        // Use RowVersion for optimistic concurrency (conditional put / update expression).
        throw new NotImplementedException();
    }

    public async Task<PagedResult<BulkOperation>> QueryAsync(BulkOperationQuery query, CancellationToken cancellationToken = default)
    {
        // Support filtering by status, operation name, date range.
        // Return paged results.
        throw new NotImplementedException();
    }
}

public sealed class DynamoRowRecordRepository : IBulkRowRecordRepository
{
    public Task CreateAsync(BulkRowRecord record, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task UpdateAsync(BulkRowRecord record, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task CreateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task UpdateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<BulkRowRecord?> GetByOperationRowStepAsync(Guid operationId, int rowNumber, int stepIndex, CancellationToken cancellationToken = default)
    {
        // Indexed lookup by (operationId, rowNumber, stepIndex).
        throw new NotImplementedException();
    }

    public Task<PagedResult<BulkRowRecord>> QueryAsync(BulkRowRecordQuery query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PagedResult<int>> QueryDistinctRowNumbersAsync(Guid operationId, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public sealed class DynamoFileRepository : IBulkFileRepository
{
    public Task<BulkFile> CreateAsync(BulkFile file, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<BulkFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task UpdateAsync(BulkFile file, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
```

### Registration

Use `MetadataStorageBuilder.UseCustom()` which accepts an `Action<IServiceCollection>`:

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseFileSystem())
    .UseMetadataStorage(ms => ms.UseCustom(s =>
    {
        // Register all three repositories. All are required.
        s.AddSingleton<IBulkOperationRepository, DynamoOperationRepository>();
        s.AddSingleton<IBulkRowRecordRepository, DynamoRowRecordRepository>();
        s.AddSingleton<IBulkFileRepository, DynamoFileRepository>();
    }))
    .UseScheduler(s => s.UseChannels()));
```

### Key Implementation Notes

- **`IBulkOperationRepository.UpdateAsync`**: Must respect `BulkOperation.RowVersion` for optimistic concurrency. Increment the version on each write and throw if the stored version does not match the expected value.
- **`IBulkRowRecordRepository.CreateBatchAsync`**: The processing pipeline writes row records in batches controlled by `BulkSharpOptions.FlushBatchSize`. Use your backend's batch-write capability for efficiency.
- **`IBulkRowRecordRepository.GetByOperationRowStepAsync`**: Used for validation record lookups during execution. Index on `(OperationId, RowNumber, StepIndex)`.
- **`IBulkRowRecordRepository.QueryDistinctRowNumbersAsync`**: Used by the dashboard to paginate rows. Must return sorted, distinct row numbers.

---

## Custom Scheduler

The scheduler controls how operations are dispatched for processing. Implement `IBulkScheduler`:

```csharp
public interface IBulkScheduler
{
    Task ScheduleBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default);
    Task CancelBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default);
}
```

### Implementation Example: Queue-Based Scheduler

```csharp
using Amazon.SQS;
using Amazon.SQS.Model;
using BulkSharp.Core.Abstractions.Processing;
using System.Text.Json;

public sealed class SqsSchedulerOptions
{
    public string QueueUrl { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueueUrl))
            throw new ArgumentException("QueueUrl is required.", nameof(QueueUrl));
    }
}

public sealed class SqsScheduler : IBulkScheduler
{
    private readonly IAmazonSQS _sqsClient;
    private readonly SqsSchedulerOptions _options;

    public SqsScheduler(IAmazonSQS sqsClient, SqsSchedulerOptions options)
    {
        _sqsClient = sqsClient;
        _options = options;
    }

    public async Task ScheduleBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new { Action = "Process", OperationId = bulkOperationId });
        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _options.QueueUrl,
            MessageBody = message,
            MessageGroupId = bulkOperationId.ToString()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelBulkOperationAsync(Guid bulkOperationId, CancellationToken cancellationToken = default)
    {
        // Cancellation strategy depends on your architecture:
        // - Set a cancellation flag in metadata storage and let the processor check it.
        // - Send a cancellation message to a separate queue/topic.
        // - Use a CancellationTokenSource registry.
        var message = JsonSerializer.Serialize(new { Action = "Cancel", OperationId = bulkOperationId });
        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _options.QueueUrl,
            MessageBody = message,
            MessageGroupId = bulkOperationId.ToString()
        }, cancellationToken).ConfigureAwait(false);
    }
}
```

### Registration

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseFileSystem())
    .UseMetadataStorage(ms => ms.UseInMemory())
    .UseScheduler(s => s.UseCustom<SqsScheduler>()));
```

`UseCustom<T>()` registers the type as `IBulkScheduler` singleton. If your scheduler needs constructor dependencies, register them before `AddBulkSharp`:

```csharp
services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
services.AddSingleton(new SqsSchedulerOptions { QueueUrl = "https://sqs.us-east-1.amazonaws.com/..." });

services.AddBulkSharp(builder => builder
    .UseScheduler(s => s.UseCustom<SqsScheduler>()));
```

### Design Considerations

- The built-in `ChannelsScheduler` runs as an `IHostedService` and processes operations in-process. If your scheduler dispatches to an external queue, you need a separate worker that reads from the queue and invokes `IBulkOperationProcessor.ProcessAsync`.
- `CancelBulkOperationAsync` must be non-destructive. The operation may already be in progress; cancellation should be cooperative.

---

## Extension Package Pattern

To distribute a custom provider as a NuGet package, follow the pattern established by `BulkSharp.Files.S3`.

### Project Structure

```
BulkSharp.Files.AzureBlob/
  BulkSharp.Files.AzureBlob.csproj
  AzureBlobStorageOptions.cs
  AzureBlobStorageProvider.cs
  AzureBlobFileStorageExtensions.cs         // IServiceCollection extension
  FileStorageBuilderExtensions.cs           // FileStorageBuilder extension
```

### 1. Options Class

Validate eagerly. Use `ValidateOnStart()` in the DI registration.

```csharp
namespace BulkSharp.Files.AzureBlob;

public sealed class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "bulksharp";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString is required.", nameof(ConnectionString));
        if (string.IsNullOrWhiteSpace(ContainerName))
            throw new ArgumentException("ContainerName is required.", nameof(ContainerName));
    }
}
```

### 2. Provider Implementation

Mark the provider `internal sealed`. Only the extension methods are public.

```csharp
namespace BulkSharp.Files.AzureBlob;

internal sealed class AzureBlobStorageProvider : IFileStorageProvider
{
    // Full implementation (see file storage section above).
}
```

### 3. IServiceCollection Extension

This is the low-level registration method. It can be called independently of BulkSharp's builder, which is useful for testing or non-standard setups.

```csharp
using BulkSharp.Core.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BulkSharp.Files.AzureBlob;

public static class AzureBlobFileStorageExtensions
{
    public static IServiceCollection AddBulkSharpAzureBlobStorage(
        this IServiceCollection services,
        Action<AzureBlobStorageOptions> configure)
    {
        services.AddOptions<AzureBlobStorageOptions>()
            .Configure(configure)
            .PostConfigure(o => o.Validate())
            .ValidateOnStart();

        services.AddSingleton<BlobContainerClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;
            var client = new BlobServiceClient(opts.ConnectionString);
            return client.GetBlobContainerClient(opts.ContainerName);
        });

        services.AddSingleton<AzureBlobStorageProvider>();
        services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<AzureBlobStorageProvider>());

        return services;
    }
}
```

### 4. FileStorageBuilder Extension

This is the user-facing API. It bridges the BulkSharp builder pattern to your package's registration.

```csharp
using BulkSharp.Builders;

namespace BulkSharp.Files.AzureBlob;

public static class FileStorageBuilderExtensions
{
    public static FileStorageBuilder UseAzureBlob(
        this FileStorageBuilder builder,
        Action<AzureBlobStorageOptions> configure)
    {
        builder.EnsureNotConfigured();
        builder.Services.AddBulkSharpAzureBlobStorage(configure);
        return builder;
    }
}
```

### 5. Consumer Usage

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseAzureBlob(opts =>
    {
        opts.ConnectionString = configuration.GetConnectionString("BlobStorage")!;
        opts.ContainerName = "bulk-operations";
    }))
    .UseMetadataStorage(ms => ms.UseSqlServer(opts =>
        opts.ConnectionString = configuration.GetConnectionString("SqlServer")!))
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 4)));
```

### Key Conventions

1. **Call `EnsureNotConfigured()` first** in your builder extension method. This enforces the single-provider guard and prevents silent misconfiguration.
2. **Register `IFileStorageProvider`** via forwarding (`sp.GetRequiredService<T>()`).
3. **Use `IOptions<T>` + `ValidateOnStart()`** for configuration. Fail at startup, not at first use.
4. **Mark providers `internal sealed`**. Only expose the extension methods and options class as public API.
5. **Namespace your builder extensions** in your package's namespace so they appear via a single `using` statement.
6. **Provide both an `IServiceCollection` extension and a `FileStorageBuilder` extension**. The former supports standalone testing; the latter integrates with the BulkSharp builder.
