using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Data.EntityFramework;

internal sealed class EntityFrameworkBulkOperationRepository(
    IDbContextFactory<BulkSharpDbContext> contextFactory) : IBulkOperationRepository
{
    public Task<BulkOperation> CreateAsync(BulkOperation bulkOperation, CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            ctx.BulkOperations.Add(bulkOperation);
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return bulkOperation;
        }, cancellationToken);

    public Task<BulkOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
            await ctx.BulkOperations.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false),
        cancellationToken);

    public async Task<BulkOperation> UpdateAsync(BulkOperation bulkOperation, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 5;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                context.BulkOperations.Update(bulkOperation);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return bulkOperation;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt >= maxRetries - 1)
                    throw;

                // Reload the full entity to merge monotonically-increasing counters.
                using var reloadContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var fresh = await reloadContext.BulkOperations
                    .AsNoTracking()
                    .FirstAsync(b => b.Id == bulkOperation.Id, cancellationToken).ConfigureAwait(false);

                // Row counters are monotonically increasing — take max to avoid data loss.
                bulkOperation.TotalRows = Math.Max(bulkOperation.TotalRows, fresh.TotalRows);
                bulkOperation.ProcessedRows = Math.Max(bulkOperation.ProcessedRows, fresh.ProcessedRows);
                bulkOperation.SuccessfulRows = Math.Max(bulkOperation.SuccessfulRows, fresh.SuccessfulRows);
                bulkOperation.FailedRows = Math.Max(bulkOperation.FailedRows, fresh.FailedRows);

                // Preserve non-counter fields from DB to avoid reverting status transitions
                // made by other threads (e.g., MarkFailed while a counter flush was in-flight).
                bulkOperation.Status = fresh.Status;
                bulkOperation.ErrorMessage = fresh.ErrorMessage;
                bulkOperation.StartedAt = fresh.StartedAt;
                bulkOperation.CompletedAt = fresh.CompletedAt;
                bulkOperation.FileId = fresh.FileId;
                bulkOperation.FileSize = fresh.FileSize;

                // RowVersion must match DB for the next attempt.
                bulkOperation.RowVersion = fresh.RowVersion;
            }
        }

        throw new InvalidOperationException("UpdateAsync failed after all retry attempts.");
    }

    public Task<PagedResult<BulkOperation>> QueryAsync(BulkOperationQuery query, CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var dbSet = ctx.BulkOperations.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(query.OperationName))
            {
                var pattern = $"%{EscapeLikePattern(query.OperationName)}%";
                dbSet = dbSet.Where(b => EF.Functions.Like(b.OperationName, pattern));
            }

            if (!string.IsNullOrEmpty(query.CreatedBy))
            {
                var pattern = $"%{EscapeLikePattern(query.CreatedBy)}%";
                dbSet = dbSet.Where(b => EF.Functions.Like(b.CreatedBy, pattern));
            }

            if (query.Status.HasValue)
                dbSet = dbSet.Where(b => b.Status == query.Status.Value);

            if (query.FromDate.HasValue)
                dbSet = dbSet.Where(b => b.CreatedAt >= query.FromDate.Value);

            if (query.ToDate.HasValue)
                dbSet = dbSet.Where(b => b.CreatedAt <= query.ToDate.Value);

            var totalCount = await dbSet.CountAsync(cancellationToken).ConfigureAwait(false);

            IQueryable<BulkOperation> sorted = (query.SortBy?.ToLowerInvariant()) switch
            {
                "operationname" => query.SortDescending
                    ? dbSet.OrderByDescending(b => b.OperationName)
                    : dbSet.OrderBy(b => b.OperationName),
                "status" => query.SortDescending
                    ? dbSet.OrderByDescending(b => b.Status)
                    : dbSet.OrderBy(b => b.Status),
                "totalrows" => query.SortDescending
                    ? dbSet.OrderByDescending(b => b.TotalRows)
                    : dbSet.OrderBy(b => b.TotalRows),
                "processedrows" => query.SortDescending
                    ? dbSet.OrderByDescending(b => b.ProcessedRows)
                    : dbSet.OrderBy(b => b.ProcessedRows),
                _ => query.SortDescending
                    ? dbSet.OrderByDescending(b => b.CreatedAt)
                    : dbSet.OrderBy(b => b.CreatedAt),
            };

            var items = await sorted
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new PagedResult<BulkOperation>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }, cancellationToken);

    private static string EscapeLikePattern(string input) =>
        input.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
