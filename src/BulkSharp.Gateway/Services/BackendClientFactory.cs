using BulkSharp.Gateway.Configuration;

namespace BulkSharp.Gateway.Services;

internal sealed class BackendClientFactory(IHttpClientFactory httpClientFactory, BulkSharpGatewayOptions options) : IBackendClientFactory
{
    public IBackendClient GetClient(string serviceName)
    {
        var http = httpClientFactory.CreateClient($"BulkSharpGateway_{serviceName}");
        return new BackendClient(http, serviceName);
    }

    public IEnumerable<IBackendClient> GetAllClients()
    {
        return options.Backends.Select(b => GetClient(b.Name));
    }
}
