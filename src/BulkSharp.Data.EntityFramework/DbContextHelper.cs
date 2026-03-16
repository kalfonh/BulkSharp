namespace BulkSharp.Data.EntityFramework;

/// <summary>
/// Eliminates the repeated context-creation boilerplate across EF repositories.
/// Every repository method follows the same pattern: create context → do work → dispose.
/// </summary>
internal static class DbContextHelper
{
    /// <summary>
    /// Creates a context, executes a void async operation, and disposes the context.
    /// Use for mutations that don't return a value (Add, Update, Delete + SaveChanges).
    /// </summary>
    public static async Task ExecuteAsync<TContext>(
        IDbContextFactory<TContext> factory,
        Func<TContext, Task> operation,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        using var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await operation(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a context, executes an async query, and returns the result.
    /// Use for reads and mutations that return a value.
    /// </summary>
    public static async Task<TResult> QueryAsync<TContext, TResult>(
        IDbContextFactory<TContext> factory,
        Func<TContext, Task<TResult>> query,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        using var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await query(context).ConfigureAwait(false);
    }
}
