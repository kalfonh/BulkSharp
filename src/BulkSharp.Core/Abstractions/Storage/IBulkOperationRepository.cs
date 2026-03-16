using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Core.Abstractions.Storage;

/// <summary>
/// Persistence for bulk operation records.
/// </summary>
public interface IBulkOperationRepository
{
    Task<BulkOperation> CreateAsync(BulkOperation bulkOperation, CancellationToken cancellationToken = default);
    Task<BulkOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BulkOperation> UpdateAsync(BulkOperation bulkOperation, CancellationToken cancellationToken = default);
    Task<PagedResult<BulkOperation>> QueryAsync(BulkOperationQuery query, CancellationToken cancellationToken = default);
}
