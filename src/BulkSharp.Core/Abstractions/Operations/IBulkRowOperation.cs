namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>Defines a bulk operation that processes rows of type <typeparamref name="TRow"/> with metadata <typeparamref name="TMetadata"/>.</summary>
public interface IBulkRowOperation<TMetadata, TRow> : IBulkOperationBase<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    Task ProcessRowAsync(
        TRow row,
        TMetadata metadata,
        CancellationToken cancellationToken = default);
}
