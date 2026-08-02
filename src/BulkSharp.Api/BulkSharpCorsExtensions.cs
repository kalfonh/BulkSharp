using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Api;

/// <summary>
/// Registers a CORS policy suitable for browser clients of the BulkSharp API.
/// </summary>
/// <remarks>
/// Only required when the front end is served from a different origin than the API —
/// which is the normal case for a single-page application. A same-origin host, including
/// one using the built-in Blazor dashboard, does not need this.
/// </remarks>
public static class BulkSharpCorsExtensions
{
    /// <summary>The name of the CORS policy registered by <see cref="AddBulkSharpCors"/>.</summary>
    public const string PolicyName = "BulkSharp";

    /// <summary>
    /// Adds a CORS policy allowing the supplied origins to call the BulkSharp API with
    /// credentials. Exposes <c>Content-Disposition</c> so browser clients can read the
    /// filename from the file, export and template download endpoints.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="allowedOrigins">
    /// Exact origins to allow, for example <c>https://admin.example.com</c>.
    /// Must not be empty and must not contain <c>*</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="allowedOrigins"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when no origin is supplied, or when a wildcard origin is supplied. A wildcard
    /// cannot be combined with credentials, and silently dropping the credentials instead
    /// would produce authentication failures that are hard to diagnose.
    /// </exception>
    public static IServiceCollection AddBulkSharpCors(
        this IServiceCollection services,
        params string[] allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        if (allowedOrigins.Length == 0)
            throw new ArgumentException("At least one origin is required.", nameof(allowedOrigins));

        if (allowedOrigins.Contains("*"))
            throw new ArgumentException(
                "Wildcard origins are not supported because the policy allows credentials. " +
                "List the exact origins instead.",
                nameof(allowedOrigins));

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("Content-Disposition")));

        return services;
    }
}
