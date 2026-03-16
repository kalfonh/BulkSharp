namespace BulkSharp.Processing.Services;

internal sealed class RowRecordPersistenceProvider(
    IBulkRowRecordRepository repository,
    IRowRecordFlushService flushService) : IRowRecordPersistenceProvider
{
    private readonly ImmediateRowRecordPersistence _immediate = new(repository);
    private readonly BatchedRowRecordPersistence _batched = new(flushService);

    public IRowRecordPersistence GetPersistence<TMetadata, TRow>(IBulkStep<TMetadata, TRow> step)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
        => step is IAsyncBulkStep<TMetadata, TRow> ? _immediate : _batched;
}
