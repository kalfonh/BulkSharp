using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Processing.Storage.InMemory;

internal sealed class InMemoryBulkRowRecordRepository : IBulkRowRecordRepository
{
    private readonly ConcurrentDictionary<Guid, BulkRowRecord> _store = new();
    private readonly ConcurrentDictionary<string, Guid> _signalKeyIndex = new();

    public Task CreateAsync(BulkRowRecord record, CancellationToken ct = default)
    {
        _store[record.Id] = record;
        if (record.SignalKey is not null)
            _signalKeyIndex[record.SignalKey] = record.Id;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(BulkRowRecord record, CancellationToken ct = default)
    {
        _store[record.Id] = record;
        if (record.SignalKey is not null)
            _signalKeyIndex[record.SignalKey] = record.Id;
        return Task.CompletedTask;
    }

    public Task CreateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            _store[record.Id] = record;
            if (record.SignalKey is not null)
                _signalKeyIndex[record.SignalKey] = record.Id;
        }
        return Task.CompletedTask;
    }

    public Task UpdateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            _store[record.Id] = record;
            if (record.SignalKey is not null)
                _signalKeyIndex[record.SignalKey] = record.Id;
        }
        return Task.CompletedTask;
    }

    public Task<BulkRowRecord?> GetBySignalKeyAsync(string signalKey, CancellationToken ct = default)
    {
        if (!_signalKeyIndex.TryGetValue(signalKey, out var id))
            return Task.FromResult<BulkRowRecord?>(null);

        if (!_store.TryGetValue(id, out var record))
            return Task.FromResult<BulkRowRecord?>(null);

        if (record.State != RowRecordState.WaitingForCompletion)
            return Task.FromResult<BulkRowRecord?>(null);

        return Task.FromResult<BulkRowRecord?>(record);
    }

    public Task<BulkRowRecord?> GetByOperationRowStepAsync(Guid operationId, int rowNumber, int stepIndex, CancellationToken ct = default)
    {
        var record = _store.Values
            .FirstOrDefault(r => r.BulkOperationId == operationId && r.RowNumber == rowNumber && r.StepIndex == stepIndex);
        return Task.FromResult(record);
    }

    public Task<PagedResult<BulkRowRecord>> QueryAsync(BulkRowRecordQuery query, CancellationToken ct = default)
    {
        var q = _store.Values.ToList()
            .Where(r => r.BulkOperationId == query.OperationId);

        if (query.RowNumbers is { Count: > 0 })
            q = q.Where(r => query.RowNumbers.Contains(r.RowNumber));
        else if (query.RowNumber.HasValue)
            q = q.Where(r => r.RowNumber == query.RowNumber.Value);

        if (!string.IsNullOrEmpty(query.RowId))
            q = q.Where(r => r.RowId is not null && r.RowId.Contains(query.RowId, StringComparison.OrdinalIgnoreCase));

        if (query.StepIndex.HasValue)
            q = q.Where(r => r.StepIndex == query.StepIndex.Value);

        if (!string.IsNullOrEmpty(query.StepName))
            q = q.Where(r => r.StepName == query.StepName);

        if (query.State.HasValue)
            q = q.Where(r => r.State == query.State.Value);

        if (query.ErrorType.HasValue)
            q = q.Where(r => r.ErrorType == query.ErrorType.Value);

        if (query.ErrorsOnly == true)
            q = q.Where(r => (r.State == RowRecordState.Failed || r.State == RowRecordState.TimedOut) && r.ErrorType is not null);

        if (query.FromRowNumber.HasValue)
            q = q.Where(r => r.RowNumber >= query.FromRowNumber.Value);

        if (query.ToRowNumber.HasValue)
            q = q.Where(r => r.RowNumber <= query.ToRowNumber.Value);

        IEnumerable<BulkRowRecord> sorted = (query.SortBy?.ToLowerInvariant()) switch
        {
            "rowid" => query.SortDescending
                ? q.OrderByDescending(r => r.RowId).ThenBy(r => r.StepIndex)
                : q.OrderBy(r => r.RowId).ThenBy(r => r.StepIndex),
            "stepname" => query.SortDescending
                ? q.OrderByDescending(r => r.StepName).ThenBy(r => r.RowNumber)
                : q.OrderBy(r => r.StepName).ThenBy(r => r.RowNumber),
            "state" => query.SortDescending
                ? q.OrderByDescending(r => r.State).ThenBy(r => r.RowNumber)
                : q.OrderBy(r => r.State).ThenBy(r => r.RowNumber),
            "errortype" => query.SortDescending
                ? q.OrderByDescending(r => r.ErrorType).ThenBy(r => r.RowNumber)
                : q.OrderBy(r => r.ErrorType).ThenBy(r => r.RowNumber),
            "createdat" => query.SortDescending
                ? q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.RowNumber)
                : q.OrderBy(r => r.CreatedAt).ThenBy(r => r.RowNumber),
            _ => query.SortDescending
                ? q.OrderByDescending(r => r.RowNumber).ThenByDescending(r => r.StepIndex)
                : q.OrderBy(r => r.RowNumber).ThenBy(r => r.StepIndex),
        };

        var all = sorted.ToList();
        var totalCount = all.Count;
        var items = all
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<BulkRowRecord>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    public Task<PagedResult<int>> QueryDistinctRowNumbersAsync(Guid operationId, int page, int pageSize, CancellationToken ct = default)
    {
        var distinct = _store.Values
            .Where(r => r.BulkOperationId == operationId)
            .Select(r => r.RowNumber)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var totalCount = distinct.Count;
        var items = distinct
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<int>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}
