namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>
/// Base interface shared by all bulk operation types, providing metadata and row validation.
/// </summary>
public interface IBulkOperationBase<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    Task ValidateMetadataAsync(TMetadata metadata, CancellationToken cancellationToken = default);
    Task ValidateRowAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);
}
