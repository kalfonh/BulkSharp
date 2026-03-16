using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class BulkOperationDiscoveryService : IBulkOperationDiscovery
{
    private readonly Dictionary<string, BulkOperationInfo> _operations;
    private readonly ILogger<BulkOperationDiscoveryService>? _logger;

    public BulkOperationDiscoveryService(
        ILogger<BulkOperationDiscoveryService>? logger = null,
        BulkOperationAssemblyScope? assemblyScope = null)
    {
        _logger = logger;
        var discovered = DiscoverAllOperations(assemblyScope?.Assemblies).ToList();
        ValidateUniqueNames(discovered);
        _operations = discovered.ToDictionary(op => op.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<BulkOperationInfo> DiscoverOperations() => _operations.Values;

    public BulkOperationInfo? GetOperation(string operationName) =>
        _operations.TryGetValue(operationName, out var info) ? info : null;

    /// <summary>
    /// Validates that all discovered operation names are unique.
    /// Throws <see cref="InvalidOperationException"/> if duplicates are found.
    /// </summary>
    internal static void ValidateUniqueNames(IEnumerable<BulkOperationInfo> operations)
    {
        var duplicates = operations
            .GroupBy(op => op.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            var details = string.Join("; ", duplicates.Select(g =>
                $"'{g.Key}' is used by: {string.Join(", ", g.Select(op => op.OperationType.FullName))}"));

            throw new InvalidOperationException(
                $"Duplicate BulkOperation names detected. Each operation must have a unique name. Duplicates: {details}");
        }
    }

    /// <summary>
    /// Scans assemblies for types decorated with <see cref="BulkOperationAttribute"/>.
    /// Static so it can be called at DI registration time without building a service provider.
    /// When <paramref name="assemblies"/> is null, all loaded assemblies are scanned.
    /// </summary>
    internal static IEnumerable<BulkOperationInfo> ScanAssemblies(
        IEnumerable<Assembly>? assemblies = null,
        ILogger? logger = null)
    {
        if (assemblies == null)
        {
            logger?.AssemblyScanningFallback();
        }

        var targetAssemblies = assemblies ?? AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in targetAssemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types)
            {
                var attribute = type.GetCustomAttribute<BulkOperationAttribute>();
                if (attribute == null) continue;

                var interfaces = type
                    .GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBulkOperationBase<,>))
                    .ToList();

                if (interfaces.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Type {type.FullName} implements multiple IBulkOperationBase interfaces, which is not supported.");
                }

                if (interfaces.Count == 0)
                {
                    logger?.OperationTypeWithoutInterface(type.FullName);
                    continue;
                }

                var genericArgs = interfaces[0].GetGenericArguments();
                var isStepBased = type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBulkPipelineOperation<,>));

                var operationName = attribute.OperationName;
                var defaultStepName = operationName;
                if (!isStepBased)
                {
                    var processMethod = type.GetMethod("ProcessRowAsync", BindingFlags.Public | BindingFlags.Instance);
                    if (processMethod != null)
                    {
                        var stepAttr = processMethod.GetCustomAttribute<BulkStepAttribute>();
                        if (stepAttr != null)
                            defaultStepName = stepAttr.Name;
                    }
                }

                // Build step retryability map from [BulkStep] attributes
                var stepRetryability = new Dictionary<string, bool>();
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var stepAttr = method.GetCustomAttribute<BulkStepAttribute>();
                    if (stepAttr != null)
                        stepRetryability[stepAttr.Name] = stepAttr.AllowOperationRetry;
                }

                yield return new BulkOperationInfo
                {
                    Name = operationName,
                    Description = attribute.Description,
                    OperationType = type,
                    MetadataType = genericArgs[0],
                    RowType = genericArgs[1],
                    IsStepBased = isStepBased,
                    DefaultStepName = defaultStepName,
                    TrackRowData = attribute.TrackRowData,
                    IsRetryable = attribute.IsRetryable,
                    StepRetryability = stepRetryability
                };
            }
        }
    }

    private IEnumerable<BulkOperationInfo> DiscoverAllOperations(IEnumerable<Assembly>? assemblies = null)
    {
        foreach (var op in ScanAssemblies(assemblies, _logger))
        {
            _logger?.DiscoveredBulkOperation(op.Name, op.OperationType.FullName);
            yield return op;
        }
    }
}
