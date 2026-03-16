namespace BulkSharp.Core.Domain.Export;

public sealed class ExportResult
{
    public required Stream Stream { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public int RowCount { get; init; }
}
