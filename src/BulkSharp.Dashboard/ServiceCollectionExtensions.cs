using BulkSharp.Core.Contracts;
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
    /// <b>Security:</b> authorization is opt-in. Endpoints are unauthorized unless the host
    /// passes policy names to <c>UseBulkSharpDashboard</c>, which governs read and mutating
    /// endpoints separately via <see cref="Core.Contracts.BulkSharpAuthorizationOptions"/>.
    /// The host application must also configure the authentication and authorization
    /// middleware (<c>app.UseAuthentication(); app.UseAuthorization();</c>).
    /// Leaving the endpoints unauthorized exposes operation and row data, which is customer
    /// data, and is appropriate only for local self-hosting.
    /// </remarks>
    public static IServiceCollection AddBulkSharpDashboard(this IServiceCollection services)
    {
        // The dashboard consumes the BulkSharp API over HTTP, so it needs the same
        // JSON contract its clients do.
        services.AddBulkSharpEndpoints();

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

        // Blazor Server has no ambient base address, so resolve it from the current
        // request. The dashboard then talks to the API exactly as an external client does.
        services.AddHttpContextAccessor();
        services.AddHttpClient<BulkSharpApiClient>((provider, client) =>
        {
            var request = provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request;
            if (request is not null)
                client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}/");
        });

        // Toasts are a rendering of operation events, read over HTTP. Implementing an
        // event handler and injecting ToastService into it only works when the UI and the
        // worker share a process, which is untrue behind a gateway.
        services.AddScoped<OperationEventToastPoller>();

        return services;
    }
}
