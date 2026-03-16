namespace BulkSharp.Gateway.Services;

public interface IBackendClientFactory
{
    IBackendClient GetClient(string serviceName);
    IEnumerable<IBackendClient> GetAllClients();
}
