namespace BulkSharp.Processing.Processors;

internal sealed class SequentialRowExecutionStrategy(
    IRowRecordFlushService rowRecordFlushService,
    IBulkRowRecordRepository rowRecordRepository,
    IBulkOperationRepository operationRepository,
    IOptions<BulkSharpOptions> options) : IRowExecutionStrategy
{
    public async Task ExecuteAsync<TMetadata, TRow>(
        BulkOperation operation,
        TMetadata metadata,
        IBulkOperationBase<TMetadata, TRow> operationInstance,
        IAsyncEnumerable<TRow> rows,
        Func<TRow, TMetadata, int, CancellationToken, Task> executeRow,
        HashSet<int>? skipRowIndexes,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        var pendingRecordUpdates = new List<BulkRowRecord>();
        var rowNumber = 0;

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            rowNumber++;

            if (skipRowIndexes?.Contains(rowNumber) == true)
                continue;

            // Retrieve the validation-phase record and mark it as processing
            var validationRecord = await rowRecordRepository.GetByOperationRowStepAsync(operation.Id, rowNumber, -1, cancellationToken).ConfigureAwait(false);
            if (validationRecord != null)
            {
                validationRecord.MarkRunning();
                pendingRecordUpdates.Add(validationRecord);
            }

            try
            {
                await executeRow(row, metadata, rowNumber, cancellationToken).ConfigureAwait(false);
                operation.RecordRowResult(true);

                if (validationRecord != null)
                {
                    validationRecord.MarkCompleted();
                }
            }
            catch (Exception ex)
            {
                operation.RecordRowResult(false);

                if (validationRecord != null)
                {
                    validationRecord.MarkFailed(ex.Message, BulkErrorType.Processing);
                }
            }

            if (rowNumber % options.Value.FlushBatchSize == 0)
            {
                if (pendingRecordUpdates.Count > 0)
                {
                    await rowRecordRepository.UpdateBatchAsync(pendingRecordUpdates, cancellationToken).ConfigureAwait(false);
                    pendingRecordUpdates.Clear();
                }

                await rowRecordFlushService.FlushAsync(cancellationToken).ConfigureAwait(false);
                await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
            }
        }

        // Final flush
        if (pendingRecordUpdates.Count > 0)
            await rowRecordRepository.UpdateBatchAsync(pendingRecordUpdates, cancellationToken).ConfigureAwait(false);

        await rowRecordFlushService.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
