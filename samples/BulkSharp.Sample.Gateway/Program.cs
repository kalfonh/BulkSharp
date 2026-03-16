using BulkSharp.Dashboard;
using BulkSharp.Gateway;
using BulkSharp.Sample.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Register the gateway — points to the backend service via Aspire service discovery
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("webapp")
    .ConfigureResilience(opts =>
    {
        opts.HttpTimeout = TimeSpan.FromSeconds(30);
        opts.RegistryRefreshInterval = TimeSpan.FromMinutes(1);
    }));

// Add Dashboard UI services (Blazor Server, Razor Pages) — but NOT the Dashboard API endpoints
builder.Services.AddBulkSharpDashboard();

var app = builder.Build();
app.MapDefaultEndpoints();

// Serve static files and configure routing
app.UseStaticFiles();
app.UseRouting();

// Map gateway API endpoints (replaces Dashboard's API endpoints with aggregated versions)
app.UseBulkSharpGateway();

// Map Blazor UI (without Dashboard API — the gateway endpoints above provide the API)
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
