namespace BulkSharp.Core.Attributes;

/// <summary>
/// Marks a method as a bulk processing step. Used for:
/// <list type="bullet">
/// <item>Naming the synthetic step in simple IBulkRowOperation (on ProcessRowAsync)</item>
/// <item>Auto-discovering steps in IBulkPipelineOperation (on step methods)</item>
/// <item>Providing metadata to Step.From() factory method</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BulkStepAttribute : Attribute
{
    public BulkStepAttribute(string name) => Name = name;

    /// <summary>Display name for the step shown in the Dashboard Row Status section.</summary>
    public string Name { get; }

    /// <summary>
    /// Execution order for auto-discovery. Lower values execute first.
    /// Use explicit values for deterministic ordering — reflection order is not guaranteed.
    /// </summary>
    public int Order { get; set; }

    /// <summary>Number of retry attempts on failure with exponential backoff. Default: 0 (no retries).</summary>
    public int MaxRetries { get; set; }

    /// <summary>Whether this step can be retried via the operation-level retry feature. Default: true.</summary>
    public bool AllowOperationRetry { get; set; } = true;
}
