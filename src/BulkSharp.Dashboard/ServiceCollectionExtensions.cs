using BulkSharp.Dashboard.Services;

namespace BulkSharp.Dashboard;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BulkSharp Dashboard UI services (Blazor Server, Razor Pages, HttpClient).
    /// The consuming application is responsible for registering BulkSharp core services
    /// via <c>services.AddBulkSharp()</c> or equivalent.
    /// </summary>
    /// <remarks>
    /// <b>Security:</b> The dashboard does NOT enforce authorization on its API endpoints
    /// (create, cancel, signal, query). The host application MUST configure authentication
    /// and authorization middleware (e.g., <c>app.UseAuthentication(); app.UseAuthorization();</c>)
    /// or apply endpoint filters to protect these routes in production.
    /// </remarks>
    public static IServiceCollection AddBulkSharpDashboard(this IServiceCollection services)
    {
        // Configure antiforgery for Blazor Server
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.SuppressXFrameOptionsHeader = false;
        });

        services
            .AddRazorPages(options =>
            {
                // Disable antiforgery for the Blazor host page
                options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
            })
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        services.AddServerSideBlazor();
        services.AddHttpClient();
        services.AddSingleton<ToastService>();

        return services;
    }
}
