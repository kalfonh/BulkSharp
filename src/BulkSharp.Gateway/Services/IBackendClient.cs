namespace BulkSharp.Gateway.Services;

public interface IBackendClient
{
    string ServiceName { get; }
    Task<HttpResponseMessage> GetOperationsAsync(CancellationToken ct = default);
    Task<HttpResponseMessage> GetOperationTemplateAsync(string name, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulksAsync(string queryString, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulkAsync(Guid id, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulkErrorsAsync(Guid id, string queryString, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulkRowsAsync(Guid id, string queryString, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulkRowItemsAsync(Guid id, string queryString, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulkStatusAsync(Guid id, CancellationToken ct = default);
    Task<HttpResponseMessage> GetBulkFileAsync(Guid id, CancellationToken ct = default);
    Task<HttpResponseMessage> PostBulkAsync(HttpContent content, string contentType, CancellationToken ct = default);
    Task<HttpResponseMessage> PostValidateAsync(HttpContent content, string contentType, CancellationToken ct = default);
    Task<HttpResponseMessage> PostCancelAsync(Guid id, CancellationToken ct = default);
    Task<HttpResponseMessage> PostSignalAsync(Guid id, string key, CancellationToken ct = default);
    Task<HttpResponseMessage> PostSignalFailAsync(Guid id, string key, HttpContent? body, CancellationToken ct = default);
}
