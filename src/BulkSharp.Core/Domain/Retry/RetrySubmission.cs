namespace BulkSharp.Core.Domain.Retry;

public sealed class RetrySubmission
{
    public Guid OperationId { get; init; }
    public int RowsSubmitted { get; init; }
    public int RowsSkipped { get; init; }
    public IReadOnlyList<string>? SkippedReasons { get; init; }
}
