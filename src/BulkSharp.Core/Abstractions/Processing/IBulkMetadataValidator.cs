using BulkSharp.Core.Attributes;

namespace BulkSharp.Core.Abstractions.Processing;

/// <summary>
/// Optional reusable metadata validator. Register via DI to compose validation logic
/// that runs before the operation's own ValidateMetadataAsync.
/// Multiple validators may be registered for the same TMetadata type.
/// </summary>
[BulkExtensionPoint]
public interface IBulkMetadataValidator<in TMetadata>
    where TMetadata : IBulkMetadata
{
    Task ValidateAsync(TMetadata metadata, CancellationToken cancellationToken = default);
}
