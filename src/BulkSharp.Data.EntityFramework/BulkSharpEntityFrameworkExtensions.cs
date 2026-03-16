namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring BulkSharp with Entity Framework storage.
/// </summary>
public static class BulkSharpEntityFrameworkExtensions
{
    /// <summary>
    /// Adds Entity Framework storage for BulkSharp using the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The type of DbContext to use.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsAction">Action to configure the DbContext options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBulkSharpEntityFramework<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? optionsAction = null)
        where TContext : BulkSharpDbContext
    {
        // Ensure a DbContext factory exists. If the caller already registered one (e.g. via
        // AddBulkSharpSqlServer), optionsAction will be null and we skip this.
        // If only AddDbContext was called, upgrade to factory registration.
        if (optionsAction != null)
        {
            services.AddDbContextFactory<TContext>(optionsAction);
        }
        else
        {
            // Guarantee the factory exists even if caller used AddDbContext instead of AddDbContextFactory.
            // Prerequisite: DbContextOptions<TContext> must be registered (via AddDbContext or manually).
            services.TryAddSingleton<IDbContextFactory<TContext>, BulkSharpFallbackContextFactory<TContext>>();
        }

        // Register the factory abstraction so repositories can create short-lived DbContext instances.
        // This makes all EF repositories thread-safe for parallel row processing.
        // When TContext IS BulkSharpDbContext, AddDbContextFactory already registered the right type —
        // wrapping it with the adapter would create a circular DI resolution.
        if (typeof(TContext) != typeof(BulkSharpDbContext))
        {
            services.AddSingleton<IDbContextFactory<BulkSharpDbContext>>(sp =>
                new BulkSharpDbContextFactoryAdapter<TContext>(sp.GetRequiredService<IDbContextFactory<TContext>>()));
        }

        services.AddScoped<TContext>(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());
        if (typeof(TContext) != typeof(BulkSharpDbContext))
        {
            services.AddScoped<BulkSharpDbContext>(sp => sp.GetRequiredService<TContext>());
        }

        // Register EF repositories — singleton-safe because they use IDbContextFactory (no shared DbContext)
        services.AddSingleton<IBulkOperationRepository, EntityFrameworkBulkOperationRepository>();
        services.AddSingleton<IBulkFileRepository, EntityFrameworkBulkFileRepository>();
        services.AddSingleton<IBulkRowRecordRepository, EntityFrameworkBulkRowRecordRepository>();
        services.AddSingleton<IBulkRowRetryHistoryRepository, EntityFrameworkBulkRowRetryHistoryRepository>();

        return services;
    }

    /// <summary>
    /// Adds Entity Framework storage for BulkSharp using SQL Server with typed options.
    /// </summary>
    public static IServiceCollection AddBulkSharpSqlServer(
        this IServiceCollection services,
        Action<SqlServerStorageOptions> configure)
    {
        var opts = new SqlServerStorageOptions();
        configure(opts);
        opts.Validate();

        services.AddDbContextFactory<BulkSharpDbContext>(options =>
            options.UseSqlServer(opts.ConnectionString, sql =>
                sql.EnableRetryOnFailure(
                    maxRetryCount: opts.MaxRetryCount,
                    maxRetryDelay: opts.MaxRetryDelay,
                    errorNumbersToAdd: null)));
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>();

        return services;
    }

    /// <summary>
    /// Adapts IDbContextFactory&lt;TContext&gt; to IDbContextFactory&lt;BulkSharpDbContext&gt;
    /// so repositories can use a single factory type regardless of the concrete context.
    /// </summary>
    private sealed class BulkSharpDbContextFactoryAdapter<TContext>(
        IDbContextFactory<TContext> inner) : IDbContextFactory<BulkSharpDbContext>
        where TContext : BulkSharpDbContext
    {
        public BulkSharpDbContext CreateDbContext() => inner.CreateDbContext();

        public async Task<BulkSharpDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            await inner.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fallback factory wrapping EF Core's PooledDbContextFactory for callers who
    /// registered via AddDbContext instead of AddDbContextFactory.
    /// </summary>
    private sealed class BulkSharpFallbackContextFactory<TContext>(
        DbContextOptions<TContext> options) : IDbContextFactory<TContext>
        where TContext : BulkSharpDbContext
    {
        private readonly PooledDbContextFactory<TContext> _inner = new(options);

        public TContext CreateDbContext() => _inner.CreateDbContext();

        public async Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            await _inner.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    }
}
