namespace BulkSharp.Data.EntityFramework;

/// <summary>
/// Durable, shared event store backed by Entity Framework.
/// </summary>
/// <remarks>
/// The in-memory store keeps its own sequence counter per process, so a horizontally scaled
/// service produces sequences that are not comparable between instances: a client polling
/// through a load balancer either stalls or skips events depending on which instance answers.
/// This implementation delegates sequencing to a database identity column, so every instance
/// shares one ordering and events survive a restart.
/// <para>
/// Events accumulate — one per lifecycle transition plus one per failed row — so a retention
/// policy is required. See <see cref="PruneAsync"/>.
/// </para>
/// </remarks>
/// <param name="contextFactory">Factory for short-lived DbContext instances.</param>
internal sealed class EntityFrameworkBulkOperationEventStore(
    IDbContextFactory<BulkSharpDbContext> contextFactory) : IBulkOperationEventStore
{
    /// <inheritdoc />
    public Task<OperationEventDto> AppendAsync(
        OperationEventDto operationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationEvent);

        return DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var record = BulkOperationEventRecord.FromDto(operationEvent);

            ctx.BulkOperationEvents.Add(record);
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Sequence is populated by the database on save.
            return record.ToDto();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationEventDto>> GetForOperationAsync(
        Guid operationId,
        long? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var query = ctx.BulkOperationEvents
                .AsNoTracking()
                .Where(e => e.OperationId == operationId);

            return await ReadAsync(query, since, limit, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationEventDto>> GetAsync(
        long? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
        {
            var query = ctx.BulkOperationEvents.AsNoTracking();

            return await ReadAsync(query, since, limit, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Deletes events older than the supplied cutoff.
    /// </summary>
    /// <remarks>
    /// Events exist to drive a notification feed, which only ever reads the recent tail.
    /// Without pruning the table grows without bound, so a host should call this on a
    /// schedule — the library does not impose one, because the right retention window
    /// depends on how the feed is consumed.
    /// </remarks>
    /// <param name="olderThan">Delete events with a timestamp strictly before this.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of events deleted.</returns>
    public Task<int> PruneAsync(DateTime olderThan, CancellationToken cancellationToken = default) =>
        DbContextHelper.QueryAsync(contextFactory, async ctx =>
            await ctx.BulkOperationEvents
                .Where(e => e.Timestamp < olderThan)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);

    private static async Task<IReadOnlyList<OperationEventDto>> ReadAsync(
        IQueryable<BulkOperationEventRecord> query,
        long? since,
        int limit,
        CancellationToken cancellationToken)
    {
        if (since.HasValue)
            query = query.Where(e => e.Sequence > since.Value);

        var records = await query
            .OrderBy(e => e.Sequence)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(r => r.ToDto()).ToList();
    }
}
