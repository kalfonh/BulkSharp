using System.Net.Http.Headers;

namespace BulkSharp.Gateway.Services;

internal sealed class BackendClient : IBackendClient
{
    private readonly HttpClient _http;

    public BackendClient(HttpClient http, string serviceName)
    {
        _http = http;
        ServiceName = serviceName;
    }

    public string ServiceName { get; }

    public Task<HttpResponseMessage> GetOperationsAsync(CancellationToken ct = default)
        => _http.GetAsync("api/operations", ct);

    public Task<HttpResponseMessage> GetOperationTemplateAsync(string name, CancellationToken ct = default)
        => _http.GetAsync($"api/operations/{Uri.EscapeDataString(name)}/template", ct);

    public Task<HttpResponseMessage> GetBulksAsync(string queryString, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkAsync(Guid id, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks/{id}", ct);

    public Task<HttpResponseMessage> GetBulkErrorsAsync(Guid id, string queryString, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks/{id}/errors{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkRowsAsync(Guid id, string queryString, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks/{id}/rows{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkRowItemsAsync(Guid id, string queryString, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks/{id}/row-items{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkStatusAsync(Guid id, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks/{id}/status", ct);

    public Task<HttpResponseMessage> GetBulkFileAsync(Guid id, CancellationToken ct = default)
        => _http.GetAsync($"api/bulks/{id}/file", HttpCompletionOption.ResponseHeadersRead, ct);

    public Task<HttpResponseMessage> PostBulkAsync(HttpContent content, string contentType, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/bulks") { Content = content };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return _http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> PostValidateAsync(HttpContent content, string contentType, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/bulks/validate") { Content = content };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return _http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> PostCancelAsync(Guid id, CancellationToken ct = default)
        => _http.PostAsync($"api/bulks/{id}/cancel", null, ct);

    public Task<HttpResponseMessage> PostSignalAsync(Guid id, string key, CancellationToken ct = default)
        => _http.PostAsync($"api/bulks/{id}/signal/{Uri.EscapeDataString(key)}", null, ct);

    public Task<HttpResponseMessage> PostSignalFailAsync(Guid id, string key, HttpContent? body, CancellationToken ct = default)
        => _http.PostAsync($"api/bulks/{id}/signal/{Uri.EscapeDataString(key)}/fail", body, ct);
}
