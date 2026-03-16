using System.Net.Http.Headers;

namespace BulkSharp.Gateway.Services;

internal sealed class BackendClient(HttpClient http, string serviceName) : IBackendClient
{
    public string ServiceName => serviceName;

    public Task<HttpResponseMessage> GetOperationsAsync(CancellationToken ct = default)
        => http.GetAsync("api/operations", ct);

    public Task<HttpResponseMessage> GetOperationTemplateAsync(string name, CancellationToken ct = default)
        => http.GetAsync($"api/operations/{Uri.EscapeDataString(name)}/template", ct);

    public Task<HttpResponseMessage> GetBulksAsync(string queryString, CancellationToken ct = default)
        => http.GetAsync($"api/bulks{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkAsync(Guid id, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}", ct);

    public Task<HttpResponseMessage> GetBulkErrorsAsync(Guid id, string queryString, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/errors{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkRowsAsync(Guid id, string queryString, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/rows{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkRowItemsAsync(Guid id, string queryString, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/row-items{queryString}", ct);

    public Task<HttpResponseMessage> GetBulkStatusAsync(Guid id, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/status", ct);

    public Task<HttpResponseMessage> GetBulkFileAsync(Guid id, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/file", HttpCompletionOption.ResponseHeadersRead, ct);

    public Task<HttpResponseMessage> PostBulkAsync(HttpContent content, string contentType, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/bulks") { Content = content };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> PostValidateAsync(HttpContent content, string contentType, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/bulks/validate") { Content = content };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> PostCancelAsync(Guid id, CancellationToken ct = default)
        => http.PostAsync($"api/bulks/{id}/cancel", null, ct);

    public Task<HttpResponseMessage> PostSignalAsync(Guid id, string key, CancellationToken ct = default)
        => http.PostAsync($"api/bulks/{id}/signal/{Uri.EscapeDataString(key)}", null, ct);

    public Task<HttpResponseMessage> PostSignalFailAsync(Guid id, string key, HttpContent? body, CancellationToken ct = default)
        => http.PostAsync($"api/bulks/{id}/signal/{Uri.EscapeDataString(key)}/fail", body, ct);

    public Task<HttpResponseMessage> PostRetryAsync(Guid id, CancellationToken ct = default)
        => http.PostAsync($"api/bulks/{id}/retry", null, ct);

    public Task<HttpResponseMessage> PostRetryRowsAsync(Guid id, HttpContent body, CancellationToken ct = default)
        => http.PostAsync($"api/bulks/{id}/retry/rows", body, ct);

    public Task<HttpResponseMessage> GetRetryEligibilityAsync(Guid id, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/retry/eligibility", ct);

    public Task<HttpResponseMessage> GetRetryHistoryAsync(Guid id, string queryString, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/retry/history{queryString}", ct);

    public Task<HttpResponseMessage> GetExportAsync(Guid id, string queryString, CancellationToken ct = default)
        => http.GetAsync($"api/bulks/{id}/export{queryString}", HttpCompletionOption.ResponseHeadersRead, ct);
}
