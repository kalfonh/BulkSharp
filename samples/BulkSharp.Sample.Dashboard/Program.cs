using BulkSharp.Dashboard;
using BulkSharp.Sample.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 5));

// Add BulkSharp Dashboard UI
builder.Services.AddBulkSharpDashboard();

// Auto-signal shipment steps for demo purposes (simulates carrier webhook callbacks)
builder.Services.AddHostedService<DemoSignalService>();

var app = builder.Build();

app.UseBulkSharpDashboard();

await app.RunAsync();
