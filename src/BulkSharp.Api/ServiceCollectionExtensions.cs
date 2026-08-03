using BulkSharp.Api;
using BulkSharp.Core.Contracts;
using Microsoft.OpenApi.Models;

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

        // Describe the endpoints so clients in any technology stack can be generated
        // from the document rather than hand-written against the docs.
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BulkSharp API",
                Version = "v1",
                Description = "HTTP API for creating, monitoring and retrying bulk data operations."
            });

            // Enums travel as strings on the wire; the document must say so or generated
            // clients emit integer enums the API will reject.
            options.SchemaFilter<StringEnumSchemaFilter>();
        });

        return services;
    }
}
