using BulkSharp.Core.Contracts;
using BulkSharp.Gateway.Routing;
using BulkSharp.Gateway.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Gateway;

public static class BulkSharpGatewayWebApplicationExtensions
{
    /// <summary>
    /// Maps the aggregated gateway endpoints with a single policy applied to reads and
    /// writes alike.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="authorizationPolicy">
    /// Policy applied to every endpoint. When null, no authorization is enforced.
    /// </param>
    public static WebApplication UseBulkSharpGateway(
        this WebApplication app,
        string? authorizationPolicy = null)
        => app.UseBulkSharpGateway(new BulkSharpAuthorizationOptions { ReadPolicy = authorizationPolicy });

    /// <summary>
    /// Maps the aggregated gateway endpoints. The route surface is identical to
    /// <c>BulkSharp.Api</c>; see ContractConformanceTests.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="authorization">
    /// Policies applied to read and mutating endpoints. When null, no authorization is
    /// enforced and the host application must configure its own middleware.
    /// </param>
    public static WebApplication UseBulkSharpGateway(
        this WebApplication app,
        BulkSharpAuthorizationOptions? authorization)
    {
        var readGroup = app.MapGroup(string.Empty);
        var operateGroup = app.MapGroup(string.Empty);

        if (authorization?.ReadPolicy is { } readPolicy)
            readGroup.RequireAuthorization(readPolicy);

        if (authorization?.EffectiveOperatePolicy is { } operatePolicy)
            operateGroup.RequireAuthorization(operatePolicy);

        return app.MapGatewayEndpoints(readGroup, operateGroup);
    }

    private static WebApplication MapGatewayEndpoints(
        this WebApplication app,
        RouteGroupBuilder readGroup,
        RouteGroupBuilder operateGroup)
    {
        // GET /api/operations - aggregated discovery
        readGroup.MapGet(BulkSharpRoutes.Operations, async (
            GatewayAggregator aggregator,
            CancellationToken ct) =>
        {
            var operations = await aggregator.AggregateDiscoveryAsync(ct);
            return Results.Ok(operations);
        });

        // GET /api/operations/{name}/template - route by operation name
        readGroup.MapGet(BulkSharpRoutes.OperationTemplate, async (
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
        readGroup.MapGet(BulkSharpRoutes.Bulks, async (
            HttpRequest request,
            GatewayAggregator aggregator,
            CancellationToken ct) =>
        {
            var qs = request.QueryString.Value ?? "";
            var result = await aggregator.AggregateListAsync(qs, ct);
            return Results.Ok(result);
        });

        // GET /api/bulks/{id} - route by SourceService
        readGroup.MapGet(BulkSharpRoutes.Bulk, async (
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
        readGroup.MapGet(BulkSharpRoutes.BulkErrors, async (
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
        readGroup.MapGet(BulkSharpRoutes.BulkRows, async (
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

        // GET /api/bulks/{id}/events - route by source
        readGroup.MapGet(BulkSharpRoutes.BulkEvents, async (
            Guid id,
            HttpRequest request,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.GetBulkEventsAsync(id, request.QueryString.Value ?? "", ct);
            return await ProxyResponseAsync(response, ct);
        });

        // GET /api/events - merged notification feed across backends
        readGroup.MapGet(BulkSharpRoutes.Events, async (
            HttpRequest request,
            GatewayAggregator aggregator,
            CancellationToken ct) =>
        {
            var events = await aggregator.AggregateEventsAsync(request.QueryString.Value ?? "", ct);
            return Results.Ok(events);
        });

        // GET /api/bulks/{id}/status
        readGroup.MapGet(BulkSharpRoutes.BulkStatus, async (
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
        readGroup.MapGet(BulkSharpRoutes.BulkFile, async (
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
        var createEndpoint = operateGroup.MapPost(BulkSharpRoutes.Bulks, async (
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
                var created = System.Text.Json.JsonSerializer.Deserialize<CreateOperationResponse>(
                    json, BulkSharpJsonSerialization.Options);

                if (created is not null && created.OperationId != Guid.Empty)
                    router.CacheSource(created.OperationId, client.ServiceName);

                return Results.Content(json, "application/json", statusCode: (int)response.StatusCode);
            }

            return Results.StatusCode((int)response.StatusCode);
        });

        // POST /api/bulks/validate - route by operationName
        operateGroup.MapPost(BulkSharpRoutes.BulksValidate, async (
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
        var cancelEndpoint = operateGroup.MapPost(BulkSharpRoutes.BulkCancel, async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.PostCancelAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });

        // POST /api/bulks/{id}/signal/{key}
        var signalEndpoint = operateGroup.MapPost(BulkSharpRoutes.BulkSignal, async (
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

        // POST /api/bulks/{id}/signal/{key}/fail
        var signalFailEndpoint = operateGroup.MapPost(BulkSharpRoutes.BulkSignalFail, async (
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

        // POST /api/bulks/{id}/retry - retry all failed rows
        var retryEndpoint = operateGroup.MapPost(BulkSharpRoutes.BulkRetry, async (
            Guid id,
            GatewayRouter router,
            CancellationToken ct) =>
        {
            var client = await router.RouteBySourceServiceAsync(id, ct);
            if (client == null) return Results.NotFound();

            using var response = await client.PostRetryAsync(id, ct);
            return await ProxyResponseAsync(response, ct);
        });

        // POST /api/bulks/{id}/retry/rows - retry specific rows
        var retryRowsEndpoint = operateGroup.MapPost(BulkSharpRoutes.BulkRetryRows, async (
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

        // GET /api/bulks/{id}/retry/eligibility
        readGroup.MapGet(BulkSharpRoutes.BulkRetryEligibility, async (
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
        readGroup.MapGet(BulkSharpRoutes.BulkRetryHistory, async (
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
        readGroup.MapGet(BulkSharpRoutes.BulkExport, async (
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
