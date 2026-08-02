using BulkSharp.Api;

namespace BulkSharp.Dashboard;

/// <summary>
/// Wires up the BulkSharp Dashboard: the Blazor Server UI plus, by default, the
/// BulkSharp HTTP API it consumes.
/// </summary>
/// <remarks>
/// The API itself lives in the <c>BulkSharp.Api</c> package. A host that wants the API
/// without any UI should reference that package alone and call <c>MapBulkSharpApi()</c>;
/// nothing Razor or Blazor is pulled in.
/// </remarks>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Configures the BulkSharp Dashboard middleware, API endpoints, and Blazor hub.
    /// Use <paramref name="configureAdditionalEndpoints"/> to register extra endpoints
    /// (e.g., sample data runners) before the Blazor fallback route.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="configureAdditionalEndpoints">Optional callback to register extra endpoints before the Blazor fallback route.</param>
    /// <param name="authorizationPolicy">
    /// Optional authorization policy name applied to mutating endpoints (create, cancel, signal, retry).
    /// When null, no authorization is enforced — the host application must configure its own middleware.
    /// </param>
    public static WebApplication UseBulkSharpDashboard(
        this WebApplication app,
        Action<WebApplication>? configureAdditionalEndpoints = null,
        string? authorizationPolicy = null)
    {
        app.UseStaticFiles();
        app.UseRouting();

        app.MapBulkSharpEndpoints(authorizationPolicy);

        // Let the host app register additional endpoints (e.g. sample data) before Blazor fallback
        configureAdditionalEndpoints?.Invoke(app);

        return app.MapDashboardUi();
    }

    /// <summary>
    /// Configures the Dashboard UI only, without mapping the BulkSharp API endpoints.
    /// Use this when the API is supplied by something else — for example when the
    /// gateway serves an aggregated API over several backends.
    /// </summary>
    /// <param name="app">The web application.</param>
    public static WebApplication UseBulkSharpDashboardUi(this WebApplication app)
    {
        app.UseStaticFiles();
        app.UseRouting();

        return app.MapDashboardUi();
    }

    private static WebApplication MapDashboardUi(this WebApplication app)
    {
        app.MapRazorPages();
        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");

        return app;
    }
}
