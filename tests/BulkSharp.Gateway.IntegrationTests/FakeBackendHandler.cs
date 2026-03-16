using System.Net;
using System.Text;
using System.Text.Json;

namespace BulkSharp.Gateway.IntegrationTests;

/// <summary>
/// A DelegatingHandler that simulates a backend service by responding to
/// known routes with canned JSON responses. No real HTTP calls are made.
/// </summary>
public sealed class FakeBackendHandler : DelegatingHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public FakeBackendHandler(string serviceName)
    {
        ServiceName = serviceName;
        InnerHandler = new HttpClientHandler();
    }

    public string ServiceName { get; }

    public FakeBackendHandler OnGet(string path, object response)
    {
        _routes[$"GET:{NormalizePath(path)}"] = _ => JsonResponse(response);
        return this;
    }

    public FakeBackendHandler OnPost(string path, object response)
    {
        _routes[$"POST:{NormalizePath(path)}"] = _ => JsonResponse(response);
        return this;
    }

    public FakeBackendHandler OnGet(string path, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _routes[$"GET:{NormalizePath(path)}"] = handler;
        return this;
    }

    public FakeBackendHandler OnGetReturn(string path, HttpStatusCode statusCode)
    {
        _routes[$"GET:{NormalizePath(path)}"] = _ => new HttpResponseMessage(statusCode);
        return this;
    }

    public FakeBackendHandler OnPostReturn(string path, HttpStatusCode statusCode)
    {
        _routes[$"POST:{NormalizePath(path)}"] = _ => new HttpResponseMessage(statusCode);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var pathAndQuery = request.RequestUri?.PathAndQuery ?? "";
        var pathOnly = request.RequestUri?.AbsolutePath ?? "";

        // Try exact match (path + query), then path-only match
        var key = $"{request.Method}:{NormalizePath(pathAndQuery)}";
        if (_routes.TryGetValue(key, out var handler))
            return Task.FromResult(handler(request));

        var keyNoQs = $"{request.Method}:{NormalizePath(pathOnly)}";
        if (_routes.TryGetValue(keyNoQs, out handler))
            return Task.FromResult(handler(request));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static string NormalizePath(string path)
    {
        // Strip leading slash for consistent matching
        return path.TrimStart('/');
    }

    private static HttpResponseMessage JsonResponse(object obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
