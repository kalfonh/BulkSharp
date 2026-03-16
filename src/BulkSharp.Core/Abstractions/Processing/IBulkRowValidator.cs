namespace BulkSharp.Core.Abstractions.Processing;

/// <summary>
/// Optional reusable row validator. Register via DI to compose validation logic
/// that runs before the operation's own ValidateRowAsync.
/// Multiple validators may be registered for the same TRow/TMetadata pair.
/// </summary>
public interface IBulkRowValidator<in TMetadata, in TRow>
    where TMetadata : IBulkMetadata
    where TRow : class, IBulkRow
{
    Task ValidateAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);
}
