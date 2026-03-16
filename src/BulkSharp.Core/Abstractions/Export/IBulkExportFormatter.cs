using BulkSharp.Core.Domain.Export;

namespace BulkSharp.Core.Abstractions.Export;

public interface IBulkExportFormatter
{
    Task<Stream> FormatReportAsync(IAsyncEnumerable<BulkExportRow> rows, ExportRequest request, CancellationToken ct = default);
    Task<Stream> FormatDataAsync(IAsyncEnumerable<BulkExportRow> rows, ExportRequest request, CancellationToken ct = default);
}
