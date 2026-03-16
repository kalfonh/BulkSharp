namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>Main service for creating, querying, and managing bulk operations.</summary>
public interface IBulkOperationService : IBulkOperationQueryService
{
    Task<Guid> CreateBulkOperationAsync<TMetadata>(
        string operationName,
        Stream fileStream,
        string fileName,
        TMetadata metadata,
        string createdBy,
        CancellationToken cancellationToken = default)
        where TMetadata : class;

    /// <summary>
    /// Creates a bulk operation using pre-serialized metadata JSON.
    /// Avoids deserialize/re-serialize round-trip when the caller already has raw JSON.
    /// </summary>
    Task<Guid> CreateBulkOperationAsync(
        string operationName,
        Stream fileStream,
        string fileName,
        string metadataJson,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task CancelBulkOperationAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<BulkValidationResult> ValidateBulkOperationAsync(
        string operationName,
        string metadataJson,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
