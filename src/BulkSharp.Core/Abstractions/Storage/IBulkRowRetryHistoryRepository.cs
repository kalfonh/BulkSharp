using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Core.Abstractions.Storage;

public interface IBulkRowRetryHistoryRepository
{
    Task CreateBatchAsync(IEnumerable<BulkRowRetryHistory> records, CancellationToken ct = default);
    Task<PagedResult<BulkRowRetryHistory>> QueryAsync(BulkRowRetryHistoryQuery query, CancellationToken ct = default);
}
