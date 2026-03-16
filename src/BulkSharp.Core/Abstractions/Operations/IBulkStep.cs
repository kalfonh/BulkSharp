namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>A single processing step within a step-based bulk operation. Supports configurable retry via MaxRetries.</summary>
public interface IBulkStep<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    string Name { get; }
    int MaxRetries { get; }
    Task ExecuteAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);
}