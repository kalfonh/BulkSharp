using BulkSharp.Builders;

namespace BulkSharp.Data.EntityFramework;

/// <summary>
/// Extension methods for configuring Entity Framework metadata storage on BulkSharp's MetadataStorageBuilder.
/// </summary>
public static class MetadataStorageBuilderExtensions
{
    /// <summary>
    /// Use SQL Server for BulkSharp metadata storage via Entity Framework.
    /// </summary>
    public static MetadataStorageBuilder UseSqlServer(
        this MetadataStorageBuilder builder,
        Action<SqlServerStorageOptions> configure)
    {
        return builder.UseCustom(services => services.AddBulkSharpSqlServer(configure));
    }

    /// <summary>
    /// Use a custom Entity Framework DbContext for BulkSharp metadata storage.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that includes BulkSharp entity configurations.</typeparam>
    /// <param name="builder">The metadata storage builder.</param>
    /// <param name="optionsAction">Optional action to configure the DbContext options.</param>
    /// <returns>The builder for chaining.</returns>
    public static MetadataStorageBuilder UseEntityFramework<TContext>(
        this MetadataStorageBuilder builder,
        Action<DbContextOptionsBuilder>? optionsAction = null)
        where TContext : BulkSharpDbContext
    {
        return builder.UseCustom(services => services.AddBulkSharpEntityFramework<TContext>(optionsAction));
    }
}
