using BulkSharp.Core.Domain.Queries;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class BulkOperationService(
    IBulkOperationRepository operationRepository,
    IBulkRowRecordRepository rowRecordRepository,
    IManagedStorageProvider storageProvider,
    IBulkScheduler scheduler,
    IBulkOperationDiscovery operationDiscovery,
    IServiceProvider serviceProvider,
    IOptions<BulkSharpOptions> options,
    ILogger<BulkOperationService> logger) : IBulkOperationService
{
    public async Task<Guid> CreateBulkOperationAsync<TMetadata>(
        string operationName,
        Stream fileStream,
        string fileName,
        TMetadata metadata,
        string createdBy,
        CancellationToken cancellationToken = default)
        where TMetadata : class
    {
        var metadataJson = JsonSerializer.Serialize(metadata, BulkSharpJsonDefaults.Options);
        return await CreateBulkOperationCoreAsync(operationName, fileStream, fileName, metadataJson, createdBy, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Guid> CreateBulkOperationCoreAsync(
        string operationName,
        Stream fileStream,
        string fileName,
        string metadataJson,
        string createdBy,
        CancellationToken cancellationToken)
    {
        logger.CreatingBulkOperation(operationName);

        // Create operation record first to avoid orphaned files if file storage succeeds but record creation fails
        var operation = new BulkOperation
        {
            OperationName = operationName,
            FileId = Guid.Empty,
            FileName = fileName,
            MetadataJson = metadataJson,
            CreatedBy = createdBy,
            Source = options.Value.ServiceName,
            Status = BulkOperationStatus.Pending
        };

        await operationRepository.CreateAsync(operation, cancellationToken).ConfigureAwait(false);

        try
        {
            var bulkFile = await storageProvider.StoreFileAsync(fileStream, fileName, createdBy, cancellationToken).ConfigureAwait(false);
            operation.FileId = bulkFile.Id;
            await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.FileStorageFailed(ex, operation.Id);
            operation.MarkFailed("File storage operation failed. Please try again.");
            await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
            throw;
        }

        try
        {
            await scheduler.ScheduleBulkOperationAsync(operation.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.SchedulingFailed(ex, operation.Id);
            operation.MarkFailed("Scheduling operation failed. Please try again.");
            await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
            throw;
        }

        logger.CreatedBulkOperation(operation.Id);
        return operation.Id;
    }

    public Task<Guid> CreateBulkOperationAsync(
        string operationName,
        Stream fileStream,
        string fileName,
        string metadataJson,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        return CreateBulkOperationCoreAsync(operationName, fileStream, fileName, metadataJson ?? "{}", createdBy, cancellationToken);
    }

    public Task<BulkOperation?> GetBulkOperationAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        operationRepository.GetByIdAsync(operationId, cancellationToken);

    public Task<PagedResult<BulkOperation>> QueryBulkOperationsAsync(BulkOperationQuery query, CancellationToken cancellationToken = default) =>
        operationRepository.QueryAsync(query, cancellationToken);

    public Task<PagedResult<BulkRowRecord>> QueryBulkRowRecordsAsync(BulkRowRecordQuery query, CancellationToken cancellationToken = default) =>
        rowRecordRepository.QueryAsync(query, cancellationToken);

    public async Task<BulkOperationStatus?> GetBulkOperationStatusAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        return operation?.Status;
    }

    public async Task CancelBulkOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation?.Status is BulkOperationStatus.Pending or BulkOperationStatus.Running)
        {
            operation.MarkCancelled();
            await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
            await scheduler.CancelBulkOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BulkValidationResult> ValidateBulkOperationAsync(
        string operationName,
        string metadataJson,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkValidationResult();

        var opInfo = operationDiscovery.GetOperation(operationName);
        if (opInfo == null)
        {
            result.MetadataErrors.Add($"Operation '{operationName}' not found");
            return result;
        }

        // Always validate file structure regardless of metadata deserialization outcome
        var validateFileMethod = ValidateFileMethodCache.GetOrAdd(
            opInfo.RowType,
            rowType => ValidateFileMethodTemplate.MakeGenericMethod(rowType));
        await ((Task)validateFileMethod.Invoke(this, [fileStream, fileName, result, cancellationToken])!)
            .ConfigureAwait(false);

        object? metadata;
        try
        {
            metadata = string.IsNullOrWhiteSpace(metadataJson)
                ? Activator.CreateInstance(opInfo.MetadataType)
                : JsonSerializer.Deserialize(metadataJson, opInfo.MetadataType, BulkSharpJsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            result.MetadataErrors.Add($"Invalid metadata JSON: {ex.Message}");
            return result;
        }

        if (metadata == null)
        {
            result.MetadataErrors.Add("Metadata deserialized to null");
            return result;
        }

        var validateMethod = ValidateMethodCache.GetOrAdd(
            (opInfo.OperationType, opInfo.MetadataType, opInfo.RowType),
            key => ValidateMethodTemplate.MakeGenericMethod(key.Item1, key.Item2, key.Item3));

        await ((Task)validateMethod.Invoke(this, [opInfo, metadata, result, cancellationToken])!)
            .ConfigureAwait(false);

        return result;
    }

    private static readonly MethodInfo ValidateMethodTemplate = typeof(BulkOperationService)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(m => m.Name == nameof(ValidateInternalAsync));

    private static readonly MethodInfo ValidateFileMethodTemplate = typeof(BulkOperationService)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(m => m.Name == nameof(ValidateFileStructureAsync));

    private static readonly ConcurrentDictionary<(Type, Type, Type), MethodInfo> ValidateMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> ValidateFileMethodCache = new();

    private async Task ValidateInternalAsync<T, TMetadata, TRow>(
        BulkOperationInfo opInfo,
        TMetadata metadata,
        BulkValidationResult result,
        CancellationToken cancellationToken)
        where T : IBulkOperationBase<TMetadata, TRow>
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        // Validate metadata via composed validators
        var metadataValidators = serviceProvider.GetServices<IBulkMetadataValidator<TMetadata>>();
        foreach (var validator in metadataValidators)
        {
            try
            {
                await validator.ValidateAsync(metadata, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.MetadataErrors.Add(ex.Message);
            }
        }

        // Validate metadata via the operation's own validation
        var operationInstance = serviceProvider.GetService(opInfo.OperationType);
        if (operationInstance is IBulkOperationBase<TMetadata, TRow> typedOperation)
        {
            try
            {
                await typedOperation.ValidateMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.MetadataErrors.Add(ex.Message);
            }
        }
    }

    private async Task ValidateFileStructureAsync<TRow>(
        Stream fileStream,
        string fileName,
        BulkValidationResult result,
        CancellationToken cancellationToken)
        where TRow : class, IBulkRow, new()
    {
        if (fileStream == Stream.Null || string.IsNullOrEmpty(fileName))
        {
            return;
        }

        try
        {
            var processorFactory = serviceProvider.GetRequiredService<IDataFormatProcessorFactory<TRow>>();
            var formatProcessor = processorFactory.GetProcessor(fileName);

            var hasRows = false;
            await foreach (var _ in formatProcessor.ProcessAsync(fileStream, cancellationToken).ConfigureAwait(false))
            {
                hasRows = true;
                // Successfully read first row — file structure is valid
                break;
            }

            if (!hasRows)
            {
                result.FileErrors.Add("File contains no data rows");
            }
        }
        catch (Exception ex)
        {
            result.FileErrors.Add($"File processing error: {ex.Message}");
        }
    }
}
