namespace BulkSharp.Processing.Abstractions;

internal interface IRowRecordPersistence
{
    Task CreateAsync(BulkRowRecord record, CancellationToken ct);
    Task UpdateAsync(BulkRowRecord record, CancellationToken ct);
}
