namespace BulkSharp.Processing.Abstractions;

internal interface IRowExecutionStrategy
{
    Task ExecuteAsync<TMetadata, TRow>(
        BulkOperation operation,
        TMetadata metadata,
        IBulkOperationBase<TMetadata, TRow> operationInstance,
        IAsyncEnumerable<TRow> rows,
        Func<TRow, TMetadata, int, CancellationToken, Task> executeRow,
        HashSet<int>? skipRowIndexes,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new();
}
