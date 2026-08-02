using BulkSharp.Core.Contracts;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the services required by the BulkSharp HTTP endpoints.</summary>
public static class BulkSharpEndpointsServiceCollectionExtensions
{
    /// <summary>
    /// Applies the BulkSharp JSON response contract — camelCase property names and
    /// string-valued enums — so generated API clients bind reliably.
    /// </summary>
    /// <remarks>
    /// Not to be confused with <c>AddBulkSharpApi</c> in the <c>BulkSharp</c> package,
    /// which selects API-only mode for the processing services (no worker infrastructure).
    /// The two are unrelated and are commonly used together.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddBulkSharpEndpoints(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
            BulkSharpJsonSerialization.Configure(options.SerializerOptions));

        return services;
    }
}
