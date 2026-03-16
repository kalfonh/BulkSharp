namespace BulkSharp.Processing.Abstractions;

internal interface IRowRecordPersistenceProvider
{
    IRowRecordPersistence GetPersistence<TMetadata, TRow>(IBulkStep<TMetadata, TRow> step)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new();
}
