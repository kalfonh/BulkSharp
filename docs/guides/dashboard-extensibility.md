# Dashboard Extensibility

The BulkSharp Dashboard supports adding custom API endpoints and pages alongside the built-in monitoring UI.

For basic dashboard setup and the built-in REST API, see [Dashboard](dashboard.md).

## Adding Custom Endpoints

`UseBulkSharpDashboard` accepts a `configureAdditionalEndpoints` callback. Endpoints registered here are mapped **before** the Blazor fallback route, so they take priority over page routing:

```csharp
app.UseBulkSharpDashboard(configureAdditionalEndpoints: app =>
{
    app.MapGet("/api/custom/health", () => Results.Ok(new { status = "healthy" }));

    app.MapPost("/api/custom/trigger", async (
        [FromBody] TriggerRequest request,
        [FromServices] IBulkOperationService operationService) =>
    {
        // Custom logic here
    });
});
```

The callback receives the same `WebApplication` instance, so you have full access to minimal API routing, DI, and middleware.

## Method Signature

```csharp
public static WebApplication UseBulkSharpDashboard(
    this WebApplication app,
    Action<WebApplication>? configureAdditionalEndpoints = null,
    string? authorizationPolicy = null)
```

| Parameter | Description |
|-----------|-------------|
| `configureAdditionalEndpoints` | Callback to register extra endpoints before the Blazor fallback route |
| `authorizationPolicy` | Optional policy name applied to mutating endpoints (create, cancel, signal). Read endpoints are unprotected by default. |

## Authorization

Pass an authorization policy to protect mutating endpoints:

```csharp
app.UseBulkSharpDashboard(authorizationPolicy: "BulkSharpAdmin");
```

This applies `RequireAuthorization("BulkSharpAdmin")` to the create, cancel, and signal endpoints. Your custom endpoints in the callback are **not** affected - apply authorization individually as needed.

## Example: Sample Data Runners

The production sample registers endpoints that let the dashboard UI trigger sample operations for testing:

```csharp
app.UseBulkSharpDashboard(configureAdditionalEndpoints: sampleApp =>
{
    // List available sample operations
    sampleApp.MapGet("/api/samples", () =>
        SampleDataProvider.GetAvailableSamples()
            .Select(kvp => new
            {
                kvp.Value.OperationName,
                kvp.Value.Description,
                kvp.Value.RowCount,
                kvp.Value.FileName
            }));

    // Run a sample operation
    sampleApp.MapPost("/api/bulks/sample", async (
        [FromBody] SampleRunRequest request,
        [FromServices] IBulkOperationService operationService) =>
    {
        var sample = SampleDataProvider.GetSample(request.OperationName);
        if (sample is null)
            return Results.NotFound($"Unknown sample: {request.OperationName}");

        using var stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(sample.FileContent));

        var operationId = await operationService.CreateBulkOperationAsync(
            sample.OperationName, stream, sample.FileName,
            sample.Metadata, "sample-runner");

        return Results.Ok(new { operationId });
    });
});
```

## Example: Webhook Receiver

Add a webhook endpoint for external systems to signal async step completion:

```csharp
app.UseBulkSharpDashboard(configureAdditionalEndpoints: app =>
{
    app.MapPost("/webhooks/carrier", (
        [FromBody] CarrierEvent evt,
        [FromServices] IBulkStepSignalService signalService) =>
    {
        var key = $"{evt.OperationId}:tracking-{evt.TrackingNumber}";
        signalService.TrySignal(key);
        return Results.Ok();
    });
});
```

## Example: Custom Admin Actions

Add operational endpoints for bulk retry or cleanup:

```csharp
app.UseBulkSharpDashboard(
    authorizationPolicy: "AdminOnly",
    configureAdditionalEndpoints: app =>
    {
        app.MapPost("/api/admin/reprocess/{id:guid}", async (
            Guid id,
            [FromServices] IBulkOperationService service,
            CancellationToken ct) =>
        {
            // Custom reprocessing logic
            var operation = await service.GetBulkOperationAsync(id, ct);
            if (operation is null)
                return Results.NotFound();

            // ... reprocess logic ...
            return Results.Ok(new { reprocessed = true });
        }).RequireAuthorization("AdminOnly");

        app.MapDelete("/api/admin/cleanup", async (
            [FromQuery] int olderThanDays,
            [FromServices] IBulkOperationRepository repo,
            CancellationToken ct) =>
        {
            // Custom cleanup logic
            return Results.Ok();
        }).RequireAuthorization("AdminOnly");
    });
```

## Full Production Example

From `BulkSharp.Sample.Production`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts =>
    {
        opts.MaxRowConcurrency = 5;
        opts.IncludeRowDataInErrors = true;
    })
    .UseFileStorage(fs => fs.UseS3(s3 =>
    {
        s3.BucketName = "bulksharp-files";
        s3.ServiceUrl = builder.Configuration["S3:ServiceUrl"];
        s3.ForcePathStyle = true;
    }))
    .UseMetadataStorage(ms => ms.UseSqlServer(sql =>
        sql.ConnectionString = connectionString))
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 8))
);

builder.Services.AddBulkSharpDashboard();

var app = builder.Build();

app.UseBulkSharpDashboard(configureAdditionalEndpoints: sampleApp =>
{
    sampleApp.MapGet("/api/samples", () =>
        SampleDataProvider.GetAvailableSamples()
            .Select(kvp => new
            {
                kvp.Value.OperationName,
                kvp.Value.Description,
                kvp.Value.RowCount,
                kvp.Value.FileName
            }));

    sampleApp.MapPost("/api/bulks/sample", async (
        [FromBody] SampleRunRequest request,
        [FromServices] IBulkOperationService operationService) =>
    {
        var sample = SampleDataProvider.GetSample(request.OperationName);
        if (sample is null)
            return Results.NotFound($"Unknown sample: {request.OperationName}");

        using var stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(sample.FileContent));

        var operationId = await operationService.CreateBulkOperationAsync(
            sample.OperationName, stream, sample.FileName,
            sample.Metadata, "sample-runner");

        return Results.Ok(new { operationId });
    });
});

app.Run();
```

## Key Points

- Custom endpoints must be registered via the `configureAdditionalEndpoints` callback, not directly on `app` after `UseBulkSharpDashboard`. The Blazor fallback route (`MapFallbackToPage`) catches all unmatched routes, so endpoints registered after it will never be reached.
- The callback runs after built-in API endpoints are mapped but before `MapBlazorHub` and the fallback page.
- Authorization on custom endpoints is your responsibility. The `authorizationPolicy` parameter only covers built-in mutating endpoints.
