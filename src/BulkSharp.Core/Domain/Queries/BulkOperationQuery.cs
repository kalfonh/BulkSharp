using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Queries;

/// <summary>
/// Flexible query object for filtering and paging bulk operations.
/// All filter properties are optional and combined with AND logic.
/// </summary>
public class BulkOperationQuery
{
    /// <summary>Filters by operation name using contains (case-insensitive) matching.</summary>
    public string? OperationName { get; set; }

    /// <summary>Filters by created-by using contains (case-insensitive) matching.</summary>
    public string? CreatedBy { get; set; }

    public BulkOperationStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    private int _page = 1;
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = 20;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : (value > 1000 ? 1000 : value);
    }

    /// <summary>
    /// Column to sort by. Allowed: "CreatedAt", "OperationName", "Status", "TotalRows", "ProcessedRows".
    /// Default: "CreatedAt".
    /// </summary>
    public string? SortBy { get; set; } = "CreatedAt";

    /// <summary>Sort descending when true. Default: true (newest first).</summary>
    public bool SortDescending { get; set; } = true;
}
