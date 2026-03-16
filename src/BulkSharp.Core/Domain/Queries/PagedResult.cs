namespace BulkSharp.Core.Domain.Queries;

/// <summary>
/// Paged result wrapper providing items plus total count for pagination UI.
/// Properties use public setters because this type crosses HTTP serialization boundaries.
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasNextPage => Page * PageSize < TotalCount;
}
