namespace BulkSharp.Processing.Abstractions;

internal interface ITypedBulkOperationProcessor<T, TMetadata, TRow>
    where T : IBulkOperationBase<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    Task ProcessOperationAsync(
        BulkOperation operation,
        T operationInstance,
        TMetadata metadata,
        CancellationToken cancellationToken = default);
}
