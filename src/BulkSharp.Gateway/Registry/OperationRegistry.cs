using System.Collections.Concurrent;

namespace BulkSharp.Gateway.Registry;

internal sealed class OperationRegistry : IOperationRegistry
{
    private readonly ConcurrentDictionary<string, string> _operationToService = new(StringComparer.OrdinalIgnoreCase);

    public string? LookupService(string operationName)
    {
        return _operationToService.TryGetValue(operationName, out var service) ? service : null;
    }

    public void UpdateOperations(string serviceName, IEnumerable<string> operationNames)
    {
        foreach (var name in operationNames)
        {
            if (_operationToService.TryGetValue(name, out var existing) && !string.Equals(existing, serviceName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Operation '{name}' is registered by both '{existing}' and '{serviceName}'. " +
                    "Each operation name must be unique across all backend services.");
            }

            _operationToService[name] = serviceName;
        }
    }

    public IReadOnlyList<OperationRegistryEntry> GetAllOperations()
    {
        return _operationToService.Select(kv => new OperationRegistryEntry(kv.Key, kv.Value)).ToList();
    }
}
