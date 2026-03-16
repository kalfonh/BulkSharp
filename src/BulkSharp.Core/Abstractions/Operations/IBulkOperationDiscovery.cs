namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>Discovers registered bulk operations by name via assembly scanning.</summary>
public interface IBulkOperationDiscovery
{
    IEnumerable<BulkOperationInfo> DiscoverOperations();
    BulkOperationInfo? GetOperation(string operationName);
}
