using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Dashboard;
using BulkSharp.Sample.Dashboard;
using BulkSharp.Sample.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add BulkSharp core services (in-memory for sample)
builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts => opts.MaxRowConcurrency = 5)
    .AddEventHandler<ToastNotificationHandler>());

// Add BulkSharp Dashboard UI
builder.Services.AddBulkSharpDashboard();

// Auto-signal shipment steps for demo purposes (simulates carrier webhook callbacks)
builder.Services.AddHostedService<DemoSignalService>();

var app = builder.Build();

// Configure BulkSharp Dashboard with sample data endpoints
app.UseBulkSharpDashboard(configureAdditionalEndpoints: sampleApp =>
{
    sampleApp.MapGet("/api/samples", () =>
    {
        return Results.Ok(SampleDataProvider.GetAvailableSamples());
    });

    sampleApp.MapPost("/api/bulks/sample", async (
        [FromBody] SampleRunRequest request,
        [FromServices] IBulkOperationService operationService) =>
    {
        var sample = SampleDataProvider.GetSample(request.OperationName);
        if (sample == null)
            return Results.BadRequest($"No sample data available for operation '{request.OperationName}'");

        try
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sample.FileContent));
            var operationId = await operationService.CreateBulkOperationAsync(
                request.OperationName, stream, sample.FileName, sample.Metadata, "Dashboard (Sample)");

            return Results.Ok(new { OperationId = operationId });
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Error running sample: {ex.Message}");
        }
    });
});

await app.RunAsync();
