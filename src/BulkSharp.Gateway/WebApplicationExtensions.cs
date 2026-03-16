using BulkSharp.Gateway.Routing;
using BulkSharp.Gateway.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Gateway;

public static class BulkSharpGatewayWebApplicationExtensions
{
    public static WebApplication UseBulkSharpGateway(
        this WebApplication app,
        string? authorizationPolicy = null)
    {
        // GET /api/operations - aggregated discovery
        app.MapGet("/api/operations", async (
            GatewayAggregator aggregator,
            CancellationToken ct) =>
        {
            var operations = await aggregator.AggregateDiscoveryAsync(ct);
            return Results.Ok(operations);
        });

        // GET /api/operations/{name}/template - route by operation name
        app.MapGet("/api/operations/{name}/template", async (
            string name,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = router.RouteByOperation(name);
            if (client == null) return Results.NotFound($"Unknown operation: {name}");

            using var response = await client.GetOperationTemplateAsync(name, ct);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/csv";
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"{name}-template.csv";
            return Results.File(bytes, contentType, fileName);
        });

        // GET /api/bulks - aggregated list
        app.MapGet("/api/bulks", async (
            HttpRequest request,
            GatewayAggregator aggregator,
            CancellationToken ct) =>
        {
            var qs = request.QueryString.Value ?? "";
            var result = await aggregator.AggregateListAsync(qs, ct);
            return Results.Ok(result);
        });

        // GET /api/bulks/{id} - route by SourceService
        app.MapGet("/api/bulks/{id:guid}", async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetBulkAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/errors
        app.MapGet("/api/bulks/{id:guid}/errors", async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetBulkErrorsAsync(id, request.QueryString.Value ?? "", ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/rows
        app.MapGet("/api/bulks/{id:guid}/rows", async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetBulkRowsAsync(id, request.QueryString.Value ?? "", ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/row-items
        app.MapGet("/api/bulks/{id:guid}/row-items", async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetBulkRowItemsAsync(id, request.QueryString.Value ?? "", ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/status
        app.MapGet("/api/bulks/{id:guid}/status", async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetBulkStatusAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/file - streamed, no buffering
        app.MapGet("/api/bulks/{id:guid}/file", async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            var response = await client.GetBulkFileAsync(id, ct);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return Results.StatusCode((int)response.StatusCode);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "file";
            return Results.File(stream, contentType, fileName);
        });

        // POST /api/bulks - create, route by operationName
        var createEndpoint = app.MapPost("/api/bulks", async (
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            request.EnableBuffering();
            var form = await request.ReadFormAsync(ct);
            var operationName = form["operationName"].ToString();

            if (string.IsNullOrEmpty(operationName))
                return Results.BadRequest("operationName is required");

            var client = router.RouteByOperation(operationName);
            if (client == null)
                return Results.BadRequest($"Unknown operation: '{operationName}'. The owning service may not be running.");

            request.Body.Position = 0;
            var content = new StreamContent(request.Body);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType!);

            using var response = await client.PostBulkAsync(content, request.ContentType!, ct);

            // Cache the new operation's Source
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("operationId", out var idProp) ||
                    doc.RootElement.TryGetProperty("OperationId", out idProp))
                {
                    if (Guid.TryParse(idProp.GetString(), out var newId))
                        router.CacheSource(newId, client.ServiceName);
                }
                return Results.Content(json, "application/json", statusCode: (int)response.StatusCode);
            }

            return Results.StatusCode((int)response.StatusCode);
        });
        if (authorizationPolicy != null) createEndpoint.RequireAuthorization(authorizationPolicy);

        // POST /api/bulks/validate - route by operationName
        app.MapPost("/api/bulks/validate", async (
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            request.EnableBuffering();
            var form = await request.ReadFormAsync(ct);
            var operationName = form["operationName"].ToString();

            if (string.IsNullOrEmpty(operationName))
                return Results.BadRequest("operationName is required");

            var client = router.RouteByOperation(operationName);
            if (client == null)
                return Results.BadRequest($"Unknown operation: '{operationName}'");

            request.Body.Position = 0;
            var content = new StreamContent(request.Body);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType!);

            using var response = await client.PostValidateAsync(content, request.ContentType!, ct);
            return await ProxyResponseAsync(response, ct);
        });

        // POST /api/bulks/{id}/cancel
        var cancelEndpoint = app.MapPost("/api/bulks/{id:guid}/cancel", async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.PostCancelAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });
        if (authorizationPolicy != null) cancelEndpoint.RequireAuthorization(authorizationPolicy);

        // POST /api/bulks/{id}/signal/{key}
        var signalEndpoint = app.MapPost("/api/bulks/{id:guid}/signal/{key}", async (
            Guid id,
            string key,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.PostSignalAsync(id, key, ct);
            return await ProxyResponseAsync(response, ct);
        });
        if (authorizationPolicy != null) signalEndpoint.RequireAuthorization(authorizationPolicy);

        // POST /api/bulks/{id}/signal/{key}/fail
        var signalFailEndpoint = app.MapPost("/api/bulks/{id:guid}/signal/{key}/fail", async (
            Guid id,
            string key,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            var body = await new StreamReader(request.Body).ReadToEndAsync(ct);
            var httpContent = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.PostSignalFailAsync(id, key, httpContent, ct);
            return await ProxyResponseAsync(response, ct);
        });
        if (authorizationPolicy != null) signalFailEndpoint.RequireAuthorization(authorizationPolicy);

        // POST /api/bulks/{id}/retry - retry all failed rows
        var retryEndpoint = app.MapPost("/api/bulks/{id:guid}/retry", async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.PostRetryAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });
        if (authorizationPolicy != null) retryEndpoint.RequireAuthorization(authorizationPolicy);

        // POST /api/bulks/{id}/retry/rows - retry specific rows
        var retryRowsEndpoint = app.MapPost("/api/bulks/{id:guid}/retry/rows", async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            var body = await new StreamReader(request.Body).ReadToEndAsync(ct);
            var httpContent = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.PostRetryRowsAsync(id, httpContent, ct);
            return await ProxyResponseAsync(response, ct);
        });
        if (authorizationPolicy != null) retryRowsEndpoint.RequireAuthorization(authorizationPolicy);

        // GET /api/bulks/{id}/retry/eligibility
        app.MapGet("/api/bulks/{id:guid}/retry/eligibility", async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetRetryEligibilityAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/retry/history
        app.MapGet("/api/bulks/{id:guid}/retry/history", async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetRetryHistoryAsync(id, request.QueryString.Value ?? "", ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/bulks/{id}/export - streamed file download
        app.MapGet("/api/bulks/{id:guid}/export", async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            var response = await client.GetExportAsync(id, request.QueryString.Value ?? "", ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                return Results.Content(error, "application/json", statusCode: (int)response.StatusCode);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "export";
            return Results.File(stream, contentType, fileName);
        });

        return app;
    }

    private static async Task<IResult> ProxyResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        return Results.Content(content, contentType, statusCode: (int)response.StatusCode);
    }
}
