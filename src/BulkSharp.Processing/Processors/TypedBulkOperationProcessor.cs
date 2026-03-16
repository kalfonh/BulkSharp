using System.Reflection;
using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Steps;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Processors;

internal sealed class TypedBulkOperationProcessor<T, TMetadata, TRow>(
    IManagedStorageProvider storageProvider,
    IDataFormatProcessorFactory<TRow> processorFactory,
    IBulkStepExecutor stepExecutor,
    IBulkStepRecordManager recordManager,
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
        if (operation.Status == BulkOperationStatus.Retrying)
        {
            await ProcessRetryAsync(operation, operationInstance, metadata, cancellationToken).ConfigureAwait(false);
            return;
        }

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
            {
                var record = await recordManager.CreateStepRecordAsync(operation.Id, rowNumber, row.RowId, steps[i].Name, i, token)
                    .ConfigureAwait(false);
                await stepExecutor.ExecuteStepAsync(steps[i], row, meta, record, recordManager, token)
                    .ConfigureAwait(false);
            }
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
        HashSet<int> failedRowIndexes = [];
        List<BulkRowRecord> pendingRecords = [];
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

    private async Task ProcessRetryAsync(
        BulkOperation operation,
        T operationInstance,
        TMetadata metadata,
        CancellationToken cancellationToken)
    {
        var opInfo = operationDiscovery.GetOperation(operation.OperationName);

        // Transition to Running
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
                PreviousStatus = BulkOperationStatus.Retrying
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.EventDispatchFailed(ex, operation.Id, nameof(BulkOperationStatusChangedEvent));
        }

        // Load retry-targeted rows (Pending with RetryAttempt >= 1)
        var retryRows = new List<BulkRowRecord>();
        var page = 1;
        while (true)
        {
            var result = await rowRecordRepository.QueryAsync(new BulkRowRecordQuery
            {
                OperationId = operation.Id,
                State = RowRecordState.Pending,
                MinRetryAttempt = 1,
                Page = page,
                PageSize = 500
            }, cancellationToken).ConfigureAwait(false);

            retryRows.AddRange(result.Items);
            if (!result.HasNextPage) break;
            page++;
        }

        if (retryRows.Count == 0)
            return;

        // Build step list for pipeline operations
        List<IBulkStep<TMetadata, TRow>>? steps = null;
        if (operationInstance is IBulkPipelineOperation<TMetadata, TRow> pipeline)
        {
            var explicitSteps = pipeline.GetSteps().ToList();
            var discoveredSteps = DiscoverStepsFromAttributes<TMetadata, TRow>(pipeline.GetType(), pipeline);
            var explicitNames = new HashSet<string>(explicitSteps.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            steps = new List<IBulkStep<TMetadata, TRow>>(explicitSteps);
            steps.AddRange(discoveredSteps.Where(s => !explicitNames.Contains(s.Name)));
        }

        var rowProcessorArray = rowProcessors.ToArray();
        var stepName = opInfo?.DefaultStepName ?? operation.OperationName;

        // Process each retry row
        foreach (var rowRecord in retryRows)
        {
            var row = JsonSerializer.Deserialize<TRow>(rowRecord.RowData!, BulkSharpJsonDefaults.Options)!;

            try
            {
                if (steps != null)
                {
                    // Pipeline: resume from RetryFromStepIndex
                    var startStep = rowRecord.RetryFromStepIndex ?? 0;
                    for (int i = startStep; i < steps.Count; i++)
                    {
                        var record = await recordManager.GetStepRecordAsync(operation.Id, rowRecord.RowNumber, i, cancellationToken)
                            .ConfigureAwait(false);

                        if (record != null)
                        {
                            await recordManager.MarkRunningAsync(record, cancellationToken).ConfigureAwait(false);
                            await stepExecutor.ExecuteStepAsync(steps[i], row, metadata, record, recordManager, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    // Single-step: reuse existing record
                    var record = await recordManager.GetStepRecordAsync(operation.Id, rowRecord.RowNumber, 0, cancellationToken)
                        .ConfigureAwait(false);

                    if (record != null)
                    {
                        await recordManager.MarkRunningAsync(record, cancellationToken).ConfigureAwait(false);

                        try
                        {
                            var rowOp = (IBulkRowOperation<TMetadata, TRow>)operationInstance;
                            await rowOp.ProcessRowAsync(row, metadata, cancellationToken).ConfigureAwait(false);
                            foreach (var rp in rowProcessorArray)
                                await rp.ProcessAsync(row, metadata, cancellationToken).ConfigureAwait(false);

                            await recordManager.MarkCompletedAsync(record, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            await recordManager.MarkFailedAsync(record, "Processing failed", BulkErrorType.Processing, cancellationToken)
                                .ConfigureAwait(false);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.RetryRowFailed(ex, rowRecord.RowNumber, operation.Id);
            }
            finally
            {
                rowRecord.RetryFromStepIndex = null;
                rowRecordFlushService.TrackUpdate(rowRecord);
            }
        }

        await rowRecordFlushService.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Recalculate counters from actual row record states
        var allRows = new List<BulkRowRecord>();
        page = 1;
        while (true)
        {
            var result = await rowRecordRepository.QueryAsync(new BulkRowRecordQuery
            {
                OperationId = operation.Id,
                Page = page,
                PageSize = 1000
            }, cancellationToken).ConfigureAwait(false);
            allRows.AddRange(result.Items);
            if (!result.HasNextPage) break;
            page++;
        }

        // For each row, take the highest StepIndex record (latest step = current state)
        var latestPerRow = allRows
            .Where(r => r.StepIndex >= 0)
            .GroupBy(r => r.RowNumber)
            .Select(g => g.OrderByDescending(r => r.StepIndex).First())
            .ToList();

        var successCount = latestPerRow.Count(r => r.State == RowRecordState.Completed);
        var failCount = latestPerRow.Count(r => r.State is RowRecordState.Failed or RowRecordState.TimedOut);
        operation.RecalculateCounters(successCount, failCount, operation.TotalRows);
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
                    x.Attr!.MaxRetries,
                    x.Attr!.AllowOperationRetry);
            })
            .ToList();
    }
}
