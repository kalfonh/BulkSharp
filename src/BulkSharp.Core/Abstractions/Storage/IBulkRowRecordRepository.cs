using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Core.Abstractions.Storage;

public interface IBulkRowRecordRepository
{
    Task CreateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken ct = default);
    Task UpdateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken ct = default);
    Task CreateAsync(BulkRowRecord record, CancellationToken ct = default);
    Task UpdateAsync(BulkRowRecord record, CancellationToken ct = default);
    Task<BulkRowRecord?> GetBySignalKeyAsync(string signalKey, CancellationToken ct = default);
    Task<BulkRowRecord?> GetByOperationRowStepAsync(Guid operationId, int rowNumber, int stepIndex, CancellationToken ct = default);
    Task<PagedResult<BulkRowRecord>> QueryAsync(BulkRowRecordQuery query, CancellationToken ct = default);
    Task<PagedResult<int>> QueryDistinctRowNumbersAsync(Guid operationId, int page, int pageSize, CancellationToken ct = default);
}
