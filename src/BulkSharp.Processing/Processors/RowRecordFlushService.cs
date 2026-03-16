using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Processors;

internal sealed class RowRecordFlushService(
    IBulkRowRecordRepository repository,
    ILogger<RowRecordFlushService> logger) : IRowRecordFlushService
{
    private readonly ConcurrentBag<BulkRowRecord> _pendingCreates = [];
    private readonly ConcurrentBag<BulkRowRecord> _pendingUpdates = [];
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    public void TrackCreate(BulkRowRecord record) => _pendingCreates.Add(record);
    public void TrackUpdate(BulkRowRecord record) => _pendingUpdates.Add(record);

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var creates = DrainBag(_pendingCreates);
            var updates = DrainBag(_pendingUpdates);

            // Deduplicate: if a record appears in both creates and updates,
            // keep only the create (which carries the latest mutated state).
            // This happens when a row is created and completed between flush cycles.
            if (creates.Count > 0 && updates.Count > 0)
            {
                var createdIds = new HashSet<Guid>(creates.Select(r => r.Id));
                updates = updates.Where(r => !createdIds.Contains(r.Id)).ToList();
            }

            if (creates.Count > 0)
            {
                try
                {
                    await repository.CreateBatchAsync(creates, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.RowRecordCreateFlushFailed(ex, creates.Count);
                }
            }

            if (updates.Count > 0)
            {
                try
                {
                    await repository.UpdateBatchAsync(updates, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.RowRecordUpdateFlushFailed(ex, updates.Count);
                }
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private static List<T> DrainBag<T>(ConcurrentBag<T> bag)
    {
        var items = new List<T>();
        while (bag.TryTake(out var item))
            items.Add(item);
        return items;
    }
}
