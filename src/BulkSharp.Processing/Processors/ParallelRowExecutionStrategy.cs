namespace BulkSharp.Processing.Processors;

internal sealed class ParallelRowExecutionStrategy(
    IRowRecordFlushService rowRecordFlushService,
    IBulkRowRecordRepository rowRecordRepository,
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
        var maxConcurrency = options.Value.MaxRowConcurrency;
        var channel = Channel.CreateBounded<(TRow Row, int RowNumber)>(
            new BoundedChannelOptions(maxConcurrency * 2)
            {
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        var pendingRecordUpdates = new ConcurrentBag<BulkRowRecord>();

        var totalRowCount = 0;
        async Task ProduceAsync()
        {
            var rowNumber = 0;
            try
            {
                await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    rowNumber++;

                    if (skipRowIndexes?.Contains(rowNumber) == true)
                        continue;

                    await channel.Writer.WriteAsync((row, rowNumber), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref totalRowCount, rowNumber);
                channel.Writer.Complete();
            }
        }

        async Task ConsumeAsync()
        {
            await foreach (var (row, rowNumber) in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
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
            }
        }

        var producerTask = ProduceAsync();
        var consumerTasks = Enumerable.Range(0, maxConcurrency).Select(_ => ConsumeAsync()).ToArray();

        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task FlushPeriodicallyAsync()
        {
            while (!flushCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), flushCts.Token).ConfigureAwait(false);
                    await DrainAndFlushAsync(pendingRecordUpdates, CancellationToken.None).ConfigureAwait(false);
                    await rowRecordFlushService.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        var flusherTask = FlushPeriodicallyAsync();

        await producerTask.ConfigureAwait(false);
        await Task.WhenAll(consumerTasks).ConfigureAwait(false);

        await flushCts.CancelAsync().ConfigureAwait(false);
        try { await flusherTask.ConfigureAwait(false); } catch (OperationCanceledException) { }

        await DrainAndFlushAsync(pendingRecordUpdates, CancellationToken.None).ConfigureAwait(false);
        await rowRecordFlushService.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task DrainAndFlushAsync(
        ConcurrentBag<BulkRowRecord> pendingRecordUpdates,
        CancellationToken cancellationToken)
    {
        List<BulkRowRecord> recordItems = [];
        while (pendingRecordUpdates.TryTake(out var item))
            recordItems.Add(item);

        if (recordItems.Count > 0)
            await rowRecordRepository.UpdateBatchAsync(recordItems, cancellationToken).ConfigureAwait(false);
    }
}
