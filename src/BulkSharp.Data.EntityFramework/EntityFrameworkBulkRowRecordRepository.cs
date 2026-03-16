using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Data.EntityFramework;

internal sealed class EntityFrameworkBulkRowRecordRepository(
    IDbContextFactory<BulkSharpDbContext> contextFactory) : IBulkRowRecordRepository
{
    public Task CreateAsync(BulkRowRecord record, CancellationToken ct = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            ctx.BulkRowRecords.Add(record);
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task UpdateAsync(BulkRowRecord record, CancellationToken ct = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            await ctx.BulkRowRecords
                .Where(r => r.Id == record.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.State, record.State)
                    .SetProperty(r => r.ErrorType, record.ErrorType)
                    .SetProperty(r => r.ErrorMessage, record.ErrorMessage)
                    .SetProperty(r => r.SignalKey, record.SignalKey)
                    .SetProperty(r => r.StartedAt, record.StartedAt)
                    .SetProperty(r => r.CompletedAt, record.CompletedAt)
                    .SetProperty(r => r.RetryAttempt, record.RetryAttempt)
                    .SetProperty(r => r.RetryFromStepIndex, record.RetryFromStepIndex)
                    .SetProperty(r => r.RowData, record.RowData),
                ct).ConfigureAwait(false);
        }, ct);

    public Task CreateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken ct = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            ctx.BulkRowRecords.AddRange(records);
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task UpdateBatchAsync(IEnumerable<BulkRowRecord> records, CancellationToken ct = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            foreach (var record in records)
            {
                ctx.Attach(record);
                var entry = ctx.Entry(record);
                entry.Property(r => r.State).IsModified = true;
                entry.Property(r => r.ErrorType).IsModified = true;
                entry.Property(r => r.ErrorMessage).IsModified = true;
                entry.Property(r => r.SignalKey).IsModified = true;
                entry.Property(r => r.StartedAt).IsModified = true;
                entry.Property(r => r.CompletedAt).IsModified = true;
                entry.Property(r => r.RetryAttempt).IsModified = true;
                entry.Property(r => r.RetryFromStepIndex).IsModified = true;
                entry.Property(r => r.RowData).IsModified = true;
            }
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task<BulkRowRecord?> GetBySignalKeyAsync(string signalKey, CancellationToken ct = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
            await ctx.BulkRowRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.SignalKey == signalKey && r.State == RowRecordState.WaitingForCompletion, ct)
                .ConfigureAwait(false),
        ct);

    public Task<BulkRowRecord?> GetByOperationRowStepAsync(Guid operationId, int rowNumber, int stepIndex, CancellationToken ct = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
            await ctx.BulkRowRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.BulkOperationId == operationId && r.RowNumber == rowNumber && r.StepIndex == stepIndex, ct)
                .ConfigureAwait(false),
        ct);

    public Task<PagedResult<BulkRowRecord>> QueryAsync(BulkRowRecordQuery query, CancellationToken ct = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var q = ctx.BulkRowRecords
                .AsNoTracking()
                .Where(r => r.BulkOperationId == query.OperationId);

            if (query.RowNumbers is { Count: > 0 })
                q = q.Where(r => query.RowNumbers.Contains(r.RowNumber));
            else if (query.RowNumber.HasValue)
                q = q.Where(r => r.RowNumber == query.RowNumber.Value);

            if (!string.IsNullOrEmpty(query.RowId))
                q = q.Where(r => r.RowId != null && EF.Functions.Like(r.RowId, $"%{query.RowId}%"));

            if (query.StepIndex.HasValue)
                q = q.Where(r => r.StepIndex == query.StepIndex.Value);

            if (!string.IsNullOrEmpty(query.StepName))
                q = q.Where(r => r.StepName == query.StepName);

            if (query.State.HasValue)
                q = q.Where(r => r.State == query.State.Value);

            if (query.ErrorType.HasValue)
                q = q.Where(r => r.ErrorType == query.ErrorType.Value);

            if (query.ErrorsOnly == true)
                q = q.Where(r => (r.State == RowRecordState.Failed || r.State == RowRecordState.TimedOut) && r.ErrorType != null);

            if (query.FromRowNumber.HasValue)
                q = q.Where(r => r.RowNumber >= query.FromRowNumber.Value);

            if (query.ToRowNumber.HasValue)
                q = q.Where(r => r.RowNumber <= query.ToRowNumber.Value);

            if (query.MinRetryAttempt.HasValue)
                q = q.Where(r => r.RetryAttempt >= query.MinRetryAttempt.Value);

            var totalCount = await q.CountAsync(ct).ConfigureAwait(false);

            IQueryable<BulkRowRecord> sorted = (query.SortBy?.ToLowerInvariant()) switch
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

            var items = await sorted
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return new PagedResult<BulkRowRecord>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }, ct);

    public Task<PagedResult<int>> QueryDistinctRowNumbersAsync(Guid operationId, int page, int pageSize, CancellationToken ct = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var query = ctx.BulkRowRecords
                .AsNoTracking()
                .Where(r => r.BulkOperationId == operationId)
                .Select(r => r.RowNumber)
                .Distinct()
                .OrderBy(n => n);

            var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return new PagedResult<int>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }, ct);
}
