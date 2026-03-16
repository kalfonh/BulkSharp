using BulkSharp.Core.Domain.Export;

namespace BulkSharp.Core.Abstractions.Operations;

public interface IBulkExportService
{
    Task<ExportResult> ExportAsync(Guid operationId, ExportRequest request, CancellationToken ct = default);
}
