namespace BulkSharp.Core.Domain.Queries;

public sealed class BulkRowRetryHistoryQuery
{
    public required Guid OperationId { get; init; }
    public int? RowNumber { get; set; }
    public int? StepIndex { get; set; }
    public int? Attempt { get; set; }

    private int _page = 1;
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = 100;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 1000);
    }
}
