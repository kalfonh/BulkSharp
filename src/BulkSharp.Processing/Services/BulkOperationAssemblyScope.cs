namespace BulkSharp.Processing.Services;

internal sealed class BulkOperationAssemblyScope(IReadOnlyList<Assembly> assemblies)
{
    public IReadOnlyList<Assembly> Assemblies { get; } = assemblies.ToArray();
}
