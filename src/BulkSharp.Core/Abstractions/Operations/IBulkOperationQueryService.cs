using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>Query-only service for retrieving bulk operation data (ISP split from IBulkOperationService).</summary>
public interface IBulkOperationQueryService
{
    Task<BulkOperation?> GetBulkOperationAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<BulkOperationStatus?> GetBulkOperationStatusAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<PagedResult<BulkOperation>> QueryBulkOperationsAsync(BulkOperationQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<BulkRowRecord>> QueryBulkRowRecordsAsync(BulkRowRecordQuery query, CancellationToken cancellationToken = default);
}
