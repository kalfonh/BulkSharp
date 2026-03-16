using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Processing.Storage.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IBulkOperationRepository"/>. Intended for testing only;
/// not suitable for production use as data is not persisted and concurrency guarantees are limited.
/// </summary>
internal sealed class InMemoryBulkOperationRepository : IBulkOperationRepository
{
    private readonly ConcurrentDictionary<Guid, BulkOperation> _operations = new();

    public Task<BulkOperation> CreateAsync(BulkOperation operation, CancellationToken cancellationToken = default)
    {
        _operations[operation.Id] = operation;
        return Task.FromResult(operation);
    }

    public Task<BulkOperation?> GetByIdAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        _operations.TryGetValue(operationId, out var operation);
        return Task.FromResult(operation);
    }

    public Task<BulkOperation> UpdateAsync(BulkOperation operation, CancellationToken cancellationToken = default)
    {
        _operations[operation.Id] = operation;
        return Task.FromResult(operation);
    }

    public Task<PagedResult<BulkOperation>> QueryAsync(BulkOperationQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = _operations.Values.ToList().AsEnumerable();

        if (!string.IsNullOrEmpty(query.OperationName))
            filtered = filtered.Where(o => o.OperationName.Contains(query.OperationName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(query.CreatedBy))
            filtered = filtered.Where(o => o.CreatedBy.Contains(query.CreatedBy, StringComparison.OrdinalIgnoreCase));

        if (query.Status.HasValue)
            filtered = filtered.Where(o => o.Status == query.Status.Value);

        if (query.FromDate.HasValue)
            filtered = filtered.Where(o => o.CreatedAt >= query.FromDate.Value);

        if (query.ToDate.HasValue)
            filtered = filtered.Where(o => o.CreatedAt <= query.ToDate.Value);

        var materialized = filtered.ToList();
        var totalCount = materialized.Count;

        IEnumerable<BulkOperation> sorted = (query.SortBy?.ToLowerInvariant()) switch
        {
            "operationname" => query.SortDescending
                ? materialized.OrderByDescending(o => o.OperationName)
                : materialized.OrderBy(o => o.OperationName),
            "status" => query.SortDescending
                ? materialized.OrderByDescending(o => o.Status)
                : materialized.OrderBy(o => o.Status),
            "totalrows" => query.SortDescending
                ? materialized.OrderByDescending(o => o.TotalRows)
                : materialized.OrderBy(o => o.TotalRows),
            "processedrows" => query.SortDescending
                ? materialized.OrderByDescending(o => o.ProcessedRows)
                : materialized.OrderBy(o => o.ProcessedRows),
            _ => query.SortDescending
                ? materialized.OrderByDescending(o => o.CreatedAt)
                : materialized.OrderBy(o => o.CreatedAt),
        };

        var items = sorted
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<BulkOperation>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }
}
