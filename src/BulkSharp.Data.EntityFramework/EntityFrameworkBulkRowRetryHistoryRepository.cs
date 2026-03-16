using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Data.EntityFramework;

internal sealed class EntityFrameworkBulkRowRetryHistoryRepository(
    IDbContextFactory<BulkSharpDbContext> contextFactory) : IBulkRowRetryHistoryRepository
{
    public Task CreateBatchAsync(IEnumerable<BulkRowRetryHistory> records, CancellationToken ct = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            ctx.BulkRowRetryHistory.AddRange(records);
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task<PagedResult<BulkRowRetryHistory>> QueryAsync(BulkRowRetryHistoryQuery query, CancellationToken ct = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var q = ctx.BulkRowRetryHistory
                .AsNoTracking()
                .Where(r => r.BulkOperationId == query.OperationId);

            if (query.RowNumber.HasValue)
                q = q.Where(r => r.RowNumber == query.RowNumber.Value);
            if (query.StepIndex.HasValue)
                q = q.Where(r => r.StepIndex == query.StepIndex.Value);
            if (query.Attempt.HasValue)
                q = q.Where(r => r.Attempt == query.Attempt.Value);

            var totalCount = await q.CountAsync(ct).ConfigureAwait(false);

            var items = await q
                .OrderBy(r => r.RowNumber)
                .ThenBy(r => r.Attempt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return new PagedResult<BulkRowRetryHistory>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }, ct);
}
