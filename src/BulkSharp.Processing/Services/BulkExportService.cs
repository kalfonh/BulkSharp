using BulkSharp.Core.Abstractions.Export;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Export;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Processing.Services;

internal sealed class BulkExportService(
    IBulkOperationRepository operationRepository,
    IBulkRowRecordRepository rowRecordRepository,
    IBulkOperationDiscovery operationDiscovery,
    IBulkExportFormatter formatter) : IBulkExportService
{
    public async Task<ExportResult> ExportAsync(Guid operationId, ExportRequest request, CancellationToken ct = default)
    {
        var operation = await operationRepository.GetByIdAsync(operationId, ct).ConfigureAwait(false);
        if (operation is null)
            throw new InvalidOperationException($"Operation {operationId} not found");

        var opInfo = operationDiscovery.GetOperation(operation.OperationName);

        if (request.Mode == ExportMode.Data && opInfo?.TrackRowData != true)
            throw new InvalidOperationException("Data export mode requires TrackRowData to be enabled");

        var rowType = opInfo?.RowType;

        var count = 0;

        async IAsyncEnumerable<BulkExportRow> StreamRows()
        {
            var page = 1;
            var seenRowNumbers = new HashSet<int>();

            while (true)
            {
                var query = new BulkRowRecordQuery
                {
                    OperationId = operationId,
                    State = request.State,
                    ErrorType = request.ErrorType,
                    StepName = request.StepName,
                    RowNumbers = request.RowNumbers,
                    Page = page,
                    PageSize = 500,
                    SortBy = "RowNumber"
                };

                var result = await rowRecordRepository.QueryAsync(query, ct).ConfigureAwait(false);
                if (result.Items.Count == 0) break;

                // Group by RowNumber, take highest StepIndex (latest step = current state)
                var grouped = result.Items
                    .Where(r => r.StepIndex >= 0)
                    .GroupBy(r => r.RowNumber)
                    .Select(g => g.OrderByDescending(r => r.StepIndex).First());

                foreach (var record in grouped)
                {
                    if (!seenRowNumbers.Add(record.RowNumber)) continue;

                    // For RowData: use this record first, fall back to validation record (StepIndex=-1)
                    var rowData = record.RowData;
                    if (string.IsNullOrEmpty(rowData) && opInfo?.TrackRowData == true)
                    {
                        var validationRecord = await rowRecordRepository.GetByOperationRowStepAsync(
                            operationId, record.RowNumber, -1, ct).ConfigureAwait(false);
                        rowData = validationRecord?.RowData;
                    }

                    count++;
                    yield return new BulkExportRow
                    {
                        RowNumber = record.RowNumber,
                        RowId = record.RowId,
                        State = record.State,
                        StepName = record.StepName,
                        StepIndex = record.StepIndex,
                        ErrorType = record.ErrorType,
                        ErrorMessage = record.ErrorMessage,
                        RetryAttempt = record.RetryAttempt,
                        CreatedAt = record.CreatedAt,
                        CompletedAt = record.CompletedAt,
                        RowData = rowData,
                        RowType = rowType
                    };
                }

                if (!result.HasNextPage) break;
                page++;
            }
        }

        var stream = request.Mode == ExportMode.Data
            ? await formatter.FormatDataAsync(StreamRows(), request, ct).ConfigureAwait(false)
            : await formatter.FormatReportAsync(StreamRows(), request, ct).ConfigureAwait(false);

        var ext = request.Format == ExportFormat.Csv ? "csv" : "json";
        var contentType = request.Format == ExportFormat.Csv ? "text/csv" : "application/json";
        var modeLabel = request.Mode == ExportMode.Data ? "data" : "report";

        return new ExportResult
        {
            Stream = stream,
            ContentType = contentType,
            FileName = $"operation-{operationId}-{modeLabel}.{ext}",
            RowCount = count
        };
    }
}
