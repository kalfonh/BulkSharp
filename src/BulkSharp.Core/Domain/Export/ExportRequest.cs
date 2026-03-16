using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Domain.Export;

public sealed class ExportRequest
{
    public ExportMode Mode { get; init; } = ExportMode.Report;
    public ExportFormat Format { get; init; } = ExportFormat.Csv;
    public RowRecordState? State { get; init; }
    public BulkErrorType? ErrorType { get; init; }
    public string? StepName { get; init; }
    public IReadOnlyList<int>? RowNumbers { get; init; }
}
