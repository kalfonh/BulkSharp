namespace BulkSharp.Processing.Abstractions;

internal interface IRowRecordFlushService
{
    void TrackCreate(BulkRowRecord record);
    void TrackUpdate(BulkRowRecord record);
    Task FlushAsync(CancellationToken ct);
}
