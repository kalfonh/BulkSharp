using BulkSharp.Core.Abstractions.Operations;

namespace BulkSharp.Core.Steps;

/// <summary>
/// An <see cref="IBulkStep{TMetadata, TRow}"/> implementation that wraps a delegate.
/// Created via <see cref="Step.Create{TMetadata, TRow}"/> or <see cref="Step.From{TMetadata, TRow}"/>.
/// </summary>
public sealed class DelegateStep<TMetadata, TRow> : IBulkStep<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    private readonly Func<TRow, TMetadata, CancellationToken, Task> _execute;

    internal DelegateStep(string name, Func<TRow, TMetadata, CancellationToken, Task> execute, int maxRetries = 0, bool allowOperationRetry = true)
    {
        Name = name;
        _execute = execute;
        MaxRetries = maxRetries;
        AllowOperationRetry = allowOperationRetry;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public int MaxRetries { get; }

    /// <inheritdoc />
    public bool AllowOperationRetry { get; }

    /// <inheritdoc />
    public Task ExecuteAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default) =>
        _execute(row, metadata, cancellationToken);
}
