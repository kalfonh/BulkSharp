using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Queries;

public sealed class BulkRowRecordQuery
{
    public required Guid OperationId { get; init; }

    public int? RowNumber { get; set; }
    public IReadOnlyList<int>? RowNumbers { get; set; }
    public string? RowId { get; set; }

    public int? StepIndex { get; set; }
    public string? StepName { get; set; }
    public RowRecordState? State { get; set; }

    public BulkErrorType? ErrorType { get; set; }
    public bool? ErrorsOnly { get; set; }

    public int? FromRowNumber { get; set; }
    public int? ToRowNumber { get; set; }

    public int? MinRetryAttempt { get; set; }

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

    public string SortBy { get; set; } = "RowNumber";
    public bool SortDescending { get; set; }
}
