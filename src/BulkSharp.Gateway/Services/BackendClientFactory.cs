using BulkSharp.Gateway.Configuration;

namespace BulkSharp.Gateway.Services;

internal sealed class BackendClientFactory : IBackendClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BulkSharpGatewayOptions _options;

    public BackendClientFactory(IHttpClientFactory httpClientFactory, BulkSharpGatewayOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public IBackendClient GetClient(string serviceName)
    {
        var http = _httpClientFactory.CreateClient($"BulkSharpGateway_{serviceName}");
        return new BackendClient(http, serviceName);
    }

    public IEnumerable<IBackendClient> GetAllClients()
    {
        return _options.Backends.Select(b => GetClient(b.Name));
    }
}
