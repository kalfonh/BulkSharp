using System.Reflection;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Attributes;

namespace BulkSharp.Core.Steps;

/// <summary>
/// Factory for creating <see cref="IBulkStep{TMetadata, TRow}"/> instances from delegates.
/// </summary>
public static class Step
{
    /// <summary>
    /// Creates a step from a name, delegate, and optional retry count.
    /// </summary>
    public static IBulkStep<TMetadata, TRow> Create<TMetadata, TRow>(
        string name,
        Func<TRow, TMetadata, CancellationToken, Task> execute,
        int maxRetries = 0)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);
        return new DelegateStep<TMetadata, TRow>(name, execute, maxRetries);
    }

    /// <summary>
    /// Creates a step by reading the <see cref="BulkStepAttribute"/> from the target method.
    /// Throws <see cref="InvalidOperationException"/> if the attribute is missing.
    /// </summary>
    public static IBulkStep<TMetadata, TRow> From<TMetadata, TRow>(
        Func<TRow, TMetadata, CancellationToken, Task> method)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        ArgumentNullException.ThrowIfNull(method);
        var attr = method.Method.GetCustomAttribute<BulkStepAttribute>()
            ?? throw new InvalidOperationException(
                $"Method '{method.Method.Name}' is missing the [BulkStep] attribute. " +
                "Use Step.Create(\"name\", lambda) for methods without the attribute.");

        return new DelegateStep<TMetadata, TRow>(attr.Name, method, attr.MaxRetries);
    }
}
