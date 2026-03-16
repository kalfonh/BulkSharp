namespace BulkSharp.Core.Abstractions.Processing;

/// <summary>
/// Optional reusable row processor. Register via DI to compose processing logic
/// that runs after the operation's own ProcessRowAsync (regular operations only).
/// Multiple processors may be registered for the same TRow/TMetadata pair.
/// </summary>
public interface IBulkRowProcessor<in TMetadata, in TRow>
    where TMetadata : IBulkMetadata
    where TRow : class, IBulkRow
{
    Task ProcessAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);
}
