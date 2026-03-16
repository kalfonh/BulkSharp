namespace BulkSharp.Processing.Services;

internal sealed class ImmediateRowRecordPersistence(IBulkRowRecordRepository repository) : IRowRecordPersistence
{
    public Task CreateAsync(BulkRowRecord record, CancellationToken ct)
        => repository.CreateAsync(record, ct);

    public Task UpdateAsync(BulkRowRecord record, CancellationToken ct)
        => repository.UpdateAsync(record, ct);
}
