using BulkSharp.Core.Domain.Files;

namespace BulkSharp.Data.EntityFramework;

internal sealed class EntityFrameworkBulkFileRepository(
    IDbContextFactory<BulkSharpDbContext> contextFactory) : IBulkFileRepository
{
    public Task<BulkFile> CreateAsync(BulkFile file, CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            ctx.BulkFiles.Add(file);
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return file;
        }, cancellationToken);

    public Task<BulkFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
            await ctx.BulkFiles
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, cancellationToken).ConfigureAwait(false),
        cancellationToken);

    public Task UpdateAsync(BulkFile file, CancellationToken cancellationToken = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            ctx.BulkFiles.Update(file);
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        DbContextHelper.ExecuteAsync(contextFactory, async ctx =>
        {
            var file = await ctx.BulkFiles.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
            if (file != null)
            {
                file.IsDeleted = true;
                file.DeletedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
}
