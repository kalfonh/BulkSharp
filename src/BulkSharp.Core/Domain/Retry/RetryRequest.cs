namespace BulkSharp.Core.Domain.Retry;

public sealed class RetryRequest
{
    public IReadOnlyList<int>? RowNumbers { get; init; }
}
