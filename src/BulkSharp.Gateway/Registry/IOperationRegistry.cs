namespace BulkSharp.Gateway.Registry;

public interface IOperationRegistry
{
    string? LookupService(string operationName);
    void UpdateOperations(string serviceName, IEnumerable<string> operationNames);
    IReadOnlyList<OperationRegistryEntry> GetAllOperations();
}
