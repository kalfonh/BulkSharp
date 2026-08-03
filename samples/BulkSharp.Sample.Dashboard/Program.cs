using BulkSharp.Api;
using BulkSharp.Dashboard;
using BulkSharp.Sample.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 5)
    .AddNotificationChannel<BulkSharp.Sample.Dashboard.Services.LogNotificationChannel>());

// Add BulkSharp Dashboard UI
builder.Services.AddBulkSharpDashboard();

// Auto-signal shipment steps for demo purposes (simulates carrier webhook callbacks)
builder.Services.AddHostedService<DemoSignalService>();

var app = builder.Build();

// Serve the OpenAPI document outside production so a front end in any technology
// stack can be generated from it. See docs/guides/building-a-custom-dashboard.md.
if (app.Environment.IsDevelopment())
{
    app.MapBulkSharpOpenApi();
}

app.UseBulkSharpDashboard();

await app.RunAsync();
