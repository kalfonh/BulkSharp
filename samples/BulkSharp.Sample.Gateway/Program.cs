using BulkSharp.Dashboard;
using BulkSharp.Gateway;
using BulkSharp.Sample.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Register the gateway
// Backend names here must match each backend's BulkSharpOptions.ServiceName for source-based routing.
// Clients can use GET /api/bulks?source=webapp to route directly to this backend (skips fan-out).
builder.Services.AddBulkSharpGateway(gw => gw
    .AddBackend("webapp")
    .ConfigureResilience(opts =>
    {
        opts.HttpTimeout = TimeSpan.FromSeconds(30);
        opts.RegistryRefreshInterval = TimeSpan.FromMinutes(1);
    }));

// Add Dashboard UI services (Blazor Server, Razor Pages)
builder.Services.AddBulkSharpDashboard();

var app = builder.Build();
app.MapDefaultEndpoints();

// The gateway supplies the aggregated API across backends; the dashboard supplies UI only.
app.UseBulkSharpGateway();
app.UseBulkSharpDashboardUi();

app.Run();
