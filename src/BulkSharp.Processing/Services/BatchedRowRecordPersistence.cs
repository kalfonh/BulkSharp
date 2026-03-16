namespace BulkSharp.Processing.Services;

internal sealed class BatchedRowRecordPersistence(IRowRecordFlushService flushService) : IRowRecordPersistence
{
    public Task CreateAsync(BulkRowRecord record, CancellationToken ct)
    {
        flushService.TrackCreate(record);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(BulkRowRecord record, CancellationToken ct)
    {
        flushService.TrackUpdate(record);
        return Task.CompletedTask;
    }
}
