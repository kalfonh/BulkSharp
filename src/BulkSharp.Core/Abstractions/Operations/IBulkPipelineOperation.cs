namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>A bulk operation that uses multiple sequential steps instead of single-pass row processing.</summary>
public interface IBulkPipelineOperation<TMetadata, TRow> : IBulkOperationBase<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    /// <summary>
    /// Returns the steps for this operation. Override to provide explicit steps.
    /// When this returns empty, the processor auto-discovers methods annotated with <see cref="BulkSharp.Core.Attributes.BulkStepAttribute"/>.
    /// </summary>
    IEnumerable<IBulkStep<TMetadata, TRow>> GetSteps() => Enumerable.Empty<IBulkStep<TMetadata, TRow>>();
}
