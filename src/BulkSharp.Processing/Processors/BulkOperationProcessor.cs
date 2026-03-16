using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Processors;

// IServiceProvider is required for runtime generic dispatch — the processor resolves
// ITypedBulkOperationProcessor<T, TMetadata, TRow> with types known only at runtime.
// This is NOT a service locator anti-pattern; it's a typed factory dispatch.
internal sealed class BulkOperationProcessor(
    IBulkOperationRepository operationRepository,
    IBulkOperationDiscovery operationDiscovery,
    IBulkOperationEventDispatcher eventDispatcher,
    ILogger<BulkOperationProcessor> logger,
    IServiceProvider serviceProvider) : IBulkOperationProcessor
{
    private static readonly ActivitySource ActivitySource = new(BulkSharpDiagnostics.ActivitySourceName);

    private static readonly MethodInfo ProcessMethodTemplate = typeof(BulkOperationProcessor)
        .GetMethod(nameof(ProcessOperationInternalAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly ConcurrentDictionary<(Type, Type, Type), MethodInfo> MethodCache = new();

    public async Task ProcessOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ProcessOperation");
        activity?.SetTag("operation.id", operationId);

        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation == null)
        {
            logger.OperationNotFound(operationId);
            return;
        }

        // Guard against re-processing already completed/failed/cancelled operations
        if (operation.Status is BulkOperationStatus.Completed or BulkOperationStatus.CompletedWithErrors or BulkOperationStatus.Failed or BulkOperationStatus.Cancelled)
        {
            logger.OperationInTerminalState(operationId, operation.Status);
            return;
        }

        // Guard against double-processing on re-queue
        if (operation.Status is BulkOperationStatus.Running or BulkOperationStatus.Validating)
        {
            logger.OperationAlreadyRunning(operationId);
            return;
        }

        try
        {
            var opInfo = operationDiscovery.GetOperation(operation.OperationName);
            if (opInfo == null)
            {
                throw new BulkProcessingException($"Operation '{operation.OperationName}' not found");
            }

            operation.MarkValidating();
            await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);

            try
            {
                await eventDispatcher.DispatchAsync(new BulkOperationStatusChangedEvent
                {
                    OperationId = operation.Id,
                    OperationName = operation.OperationName,
                    Status = BulkOperationStatus.Validating,
                    PreviousStatus = BulkOperationStatus.Pending
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.EventDispatchFailed(ex, operation.Id, nameof(BulkOperationStatusChangedEvent));
            }

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["OperationId"] = operationId,
                ["OperationName"] = operation.OperationName
            });

            await ProcessOperationWithOperation(operation, opInfo, cancellationToken).ConfigureAwait(false);

            operation.MarkCompleted();
        }
        catch (OperationCanceledException)
        {
            logger.OperationCancelled(operationId);
            operation.MarkCancelled();
        }
        catch (Exception ex)
        {
            logger.OperationProcessingError(ex, operationId);
            operation.MarkFailed(ex.Message);
        }
        finally
        {
            // Use CancellationToken.None — terminal state must persist even if the original token is cancelled.
            // Wrap in try/catch — if concurrency retries exhaust under heavy load, the operation
            // status will be stale in DB but the processor must not crash.
            try
            {
                await operationRepository.UpdateAsync(operation, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.FinalStatePersistFailed(ex, operationId);
            }

            // Dispatch terminal event — failures here must never prevent state persistence above
            try
            {
                var terminalEvent = operation.ToTerminalEvent();
                if (terminalEvent != null)
                    await eventDispatcher.DispatchAsync(terminalEvent, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.EventDispatchFailed(ex, operationId, "TerminalEvent");
            }
        }
    }

    private async Task ProcessOperationWithOperation(BulkOperation operation, BulkOperationInfo opInfo, CancellationToken cancellationToken)
    {
        var operationInstance = serviceProvider.GetService(opInfo.OperationType);
        if (operationInstance == null)
        {
            throw new BulkProcessingException($"Could not resolve operation of type {opInfo.OperationType.Name}");
        }

        await DispatchToTypedProcessor(operation, operationInstance, opInfo, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchToTypedProcessor(
        BulkOperation operation,
        object operationInstance,
        BulkOperationInfo opInfo,
        CancellationToken cancellationToken)
    {
        var operationType = opInfo.OperationType;
        var processMethod = MethodCache.GetOrAdd(
            (operationType, opInfo.MetadataType, opInfo.RowType),
            _ => ProcessMethodTemplate.MakeGenericMethod(operationType, opInfo.MetadataType, opInfo.RowType));

        var metadata = JsonSerializer.Deserialize(operation.MetadataJson, opInfo.MetadataType, BulkSharpJsonDefaults.Options);
        if (metadata == null)
            throw new BulkProcessingException("Failed to deserialize operation metadata");

        await ((Task)processMethod.Invoke(this, [operation, operationInstance, metadata, cancellationToken])!).ConfigureAwait(false);
    }

    private async Task ProcessOperationInternalAsync<T, TMetadata, TRow>(
        BulkOperation operation,
        T operationInstance,
        TMetadata metadata,
        CancellationToken cancellationToken)
        where T : IBulkOperationBase<TMetadata, TRow>
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        var processor = serviceProvider.GetRequiredService<ITypedBulkOperationProcessor<T, TMetadata, TRow>>();
        await processor.ProcessOperationAsync(operation, operationInstance, metadata, cancellationToken).ConfigureAwait(false);
    }
}
