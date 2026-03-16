using System.Reflection;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Steps;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Processors;

internal sealed class TypedBulkOperationProcessor<T, TMetadata, TRow>(
    IManagedStorageProvider storageProvider,
    IDataFormatProcessorFactory<TRow> processorFactory,
    IBulkStepExecutor stepExecutor,
    IEnumerable<IBulkMetadataValidator<TMetadata>> metadataValidators,
    IRowValidationPipeline<TMetadata, TRow> validationPipeline,
    IEnumerable<IBulkRowProcessor<TMetadata, TRow>> rowProcessors,
    IRowExecutionStrategy rowExecutionStrategy,
    IBulkOperationDiscovery operationDiscovery,
    IBulkRowRecordRepository rowRecordRepository,
    IBulkOperationRepository operationRepository,
    IBulkOperationEventDispatcher eventDispatcher,
    IRowRecordFlushService rowRecordFlushService,
    IOptions<BulkSharpOptions> options,
    ILogger<TypedBulkOperationProcessor<T, TMetadata, TRow>> logger)
    : ITypedBulkOperationProcessor<T, TMetadata, TRow>
    where T : IBulkOperationBase<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    public async Task ProcessOperationAsync(
        BulkOperation operation,
        T operationInstance,
        TMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        // Metadata validation
        foreach (var validator in metadataValidators)
            await validator.ValidateAsync(metadata, cancellationToken).ConfigureAwait(false);

        await operationInstance.ValidateMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);

        // Resolve operation info for row data tracking
        var opInfo = operationDiscovery.GetOperation(operation.OperationName);
        var trackRowData = opInfo?.TrackRowData ?? false;

        // -- Validating phase: stream file, validate rows, create BulkRowRecords --
        HashSet<int> failedRowIndexes;
        await using (var validationStream = await storageProvider.RetrieveFileAsync(operation.FileId, cancellationToken).ConfigureAwait(false))
        {
            failedRowIndexes = await ValidateAndPrepareRowsAsync(
                operation, operationInstance, metadata, validationStream, trackRowData, cancellationToken).ConfigureAwait(false);
        }

        // -- Transition to Running --
        operation.MarkRunning();
        await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
        logger.TransitioningToProcessing(operation.Id);

        try
        {
            await eventDispatcher.DispatchAsync(new BulkOperationStatusChangedEvent
            {
                OperationId = operation.Id,
                OperationName = operation.OperationName,
                Status = BulkOperationStatus.Running,
                PreviousStatus = BulkOperationStatus.Validating
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.EventDispatchFailed(ex, operation.Id, nameof(BulkOperationStatusChangedEvent));
        }

        // -- Processing phase: re-stream file, execute valid rows --
        await using var fileStream = await storageProvider.RetrieveFileAsync(operation.FileId, cancellationToken).ConfigureAwait(false);
        var processor = processorFactory.GetProcessor(operation.FileName);
        var rows = processor.ProcessAsync(fileStream, cancellationToken);

        // Build the row execution delegate
        var executeRow = CreateExecuteDelegate(operationInstance, operation, opInfo);

        await rowExecutionStrategy.ExecuteAsync(operation, metadata, operationInstance, rows, executeRow, failedRowIndexes, cancellationToken)
            .ConfigureAwait(false);
    }

    private Func<TRow, TMetadata, int, CancellationToken, Task> CreateExecuteDelegate(
        T operationInstance, BulkOperation operation, BulkOperationInfo? opInfo)
    {
        if (operationInstance is IBulkPipelineOperation<TMetadata, TRow> pipeline)
            return CreatePipelineDelegate(pipeline, operation);

        return CreateRowDelegate((IBulkRowOperation<TMetadata, TRow>)operationInstance, operation, opInfo);
    }

    private Func<TRow, TMetadata, int, CancellationToken, Task> CreatePipelineDelegate(
        IBulkPipelineOperation<TMetadata, TRow> stepBased, BulkOperation operation)
    {
        var explicitSteps = stepBased.GetSteps().ToList();
        var discoveredSteps = DiscoverStepsFromAttributes<TMetadata, TRow>(stepBased.GetType(), stepBased);

        // Merge: explicit GetSteps() results + discovered [BulkStep] methods not already covered by name
        var explicitNames = new HashSet<string>(explicitSteps.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var merged = new List<IBulkStep<TMetadata, TRow>>(explicitSteps);
        merged.AddRange(discoveredSteps.Where(s => !explicitNames.Contains(s.Name)));
        var steps = merged;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.ProcessingStepBasedOperation(operation.Id, steps.Count, string.Join(", ", steps.Select(s => s.Name)));
        }

        return async (row, meta, rowNumber, token) =>
        {
            for (int i = 0; i < steps.Count; i++)
                await stepExecutor.ExecuteStepAsync(steps[i], row, meta, operation.Id, rowNumber, i, token)
                    .ConfigureAwait(false);
        };
    }

    private Func<TRow, TMetadata, int, CancellationToken, Task> CreateRowDelegate(
        IBulkRowOperation<TMetadata, TRow> rowOperation, BulkOperation operation, BulkOperationInfo? opInfo)
    {
        var rowProcessorArray = rowProcessors.ToArray();
        var stepName = opInfo?.DefaultStepName ?? operation.OperationName;

        return async (row, meta, rowNumber, token) =>
        {
            var record = BulkRowRecord.CreateStep(operation.Id, rowNumber, row.RowId, stepName, 0);
            rowRecordFlushService.TrackCreate(record);

            try
            {
                await rowOperation.ProcessRowAsync(row, meta, token).ConfigureAwait(false);
                foreach (var rp in rowProcessorArray)
                    await rp.ProcessAsync(row, meta, token).ConfigureAwait(false);

                record.MarkCompleted();
            }
            catch
            {
                record.MarkFailed("Processing failed", BulkErrorType.Processing);
                throw;
            }
            finally
            {
                rowRecordFlushService.TrackUpdate(record);
            }
        };
    }

    private async Task<HashSet<int>> ValidateAndPrepareRowsAsync(
        BulkOperation operation,
        T operationInstance,
        TMetadata metadata,
        Stream fileStream,
        bool trackRowData,
        CancellationToken cancellationToken)
    {
        logger.ValidatingPhaseStarted(operation.Id);

        var dataProcessor = processorFactory.GetProcessor(operation.FileName);
        var failedRowIndexes = new HashSet<int>();
        var pendingRecords = new List<BulkRowRecord>();
        var rowIndex = 0;
        var batchSize = options.Value.FlushBatchSize;

        await foreach (var row in dataProcessor.ProcessAsync(fileStream, cancellationToken).ConfigureAwait(false))
        {
            rowIndex++;
            string? serializedData = trackRowData ? JsonSerializer.Serialize(row, BulkSharpJsonDefaults.Options) : null;
            var record = BulkRowRecord.CreateValidation(operation.Id, rowIndex, row.RowId, serializedData);

            var error = await validationPipeline.ValidateRowAsync(
                row, metadata, operationInstance, rowIndex, cancellationToken).ConfigureAwait(false);

            if (error != null)
            {
                record.MarkFailed(error.ErrorMessage, error.ErrorType);
                failedRowIndexes.Add(rowIndex);
                operation.RecordRowResult(success: false);
            }

            pendingRecords.Add(record);

            if (pendingRecords.Count >= batchSize)
            {
                await rowRecordRepository.CreateBatchAsync(pendingRecords, cancellationToken).ConfigureAwait(false);
                pendingRecords.Clear();

                await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
            }
        }

        // Final flush
        if (pendingRecords.Count > 0)
            await rowRecordRepository.CreateBatchAsync(pendingRecords, cancellationToken).ConfigureAwait(false);

        operation.SetTotalRows(rowIndex);
        logger.ValidatingPhaseComplete(operation.Id, rowIndex, failedRowIndexes.Count);

        return failedRowIndexes;
    }

    internal static List<IBulkStep<TStepMeta, TStepRow>> DiscoverStepsFromAttributes<TStepMeta, TStepRow>(
        Type operationType, object operationInstance)
        where TStepMeta : IBulkMetadata, new()
        where TStepRow : class, IBulkRow, new()
    {
        return operationType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => (Method: m, Attr: m.GetCustomAttribute<BulkStepAttribute>()))
            .Where(x => x.Attr != null)
            .Where(x =>
            {
                var p = x.Method.GetParameters();
                return p.Length == 3
                    && p[0].ParameterType == typeof(TStepRow)
                    && p[1].ParameterType == typeof(TStepMeta)
                    && p[2].ParameterType == typeof(CancellationToken)
                    && x.Method.ReturnType == typeof(Task);
            })
            .OrderBy(x => x.Attr!.Order)
            .Select(x =>
            {
                var method = x.Method;
                return (IBulkStep<TStepMeta, TStepRow>)new DelegateStep<TStepMeta, TStepRow>(
                    x.Attr!.Name,
                    (row, meta, ct) => (Task)method.Invoke(operationInstance, [row, meta, ct])!,
                    x.Attr!.MaxRetries);
            })
            .ToList();
    }
}
