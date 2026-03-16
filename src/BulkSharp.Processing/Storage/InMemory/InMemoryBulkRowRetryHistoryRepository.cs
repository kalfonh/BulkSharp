using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Processing.Storage.InMemory;

internal sealed class InMemoryBulkRowRetryHistoryRepository : IBulkRowRetryHistoryRepository
{
    private readonly ConcurrentDictionary<Guid, BulkRowRetryHistory> _store = new();

    public Task CreateBatchAsync(IEnumerable<BulkRowRetryHistory> records, CancellationToken ct = default)
    {
        foreach (var record in records)
            _store[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<PagedResult<BulkRowRetryHistory>> QueryAsync(BulkRowRetryHistoryQuery query, CancellationToken ct = default)
    {
        var q = _store.Values.Where(r => r.BulkOperationId == query.OperationId);

        if (query.RowNumber.HasValue)
            q = q.Where(r => r.RowNumber == query.RowNumber.Value);
        if (query.StepIndex.HasValue)
            q = q.Where(r => r.StepIndex == query.StepIndex.Value);
        if (query.Attempt.HasValue)
            q = q.Where(r => r.Attempt == query.Attempt.Value);

        var all = q.OrderBy(r => r.RowNumber).ThenBy(r => r.Attempt).ToList();
        var items = all.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        return Task.FromResult(new PagedResult<BulkRowRetryHistory>
        {
            Items = items,
            TotalCount = all.Count,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }
}
