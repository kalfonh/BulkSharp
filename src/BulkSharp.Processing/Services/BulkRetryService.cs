using BulkSharp.Core.Domain.Queries;
using BulkSharp.Core.Domain.Retry;
using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class BulkRetryService(
    IBulkOperationRepository operationRepository,
    IBulkRowRecordRepository rowRecordRepository,
    IBulkRowRetryHistoryRepository retryHistoryRepository,
    IBulkOperationDiscovery operationDiscovery,
    IBulkScheduler scheduler,
    IOptions<BulkSharpOptions> options,
    ILogger<BulkRetryService> logger) : IBulkRetryService
{
    public async Task<RetryEligibility> CanRetryAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await operationRepository.GetByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation is null)
            return RetryEligibility.Ineligible("Operation not found");

        if (operation.Status != BulkOperationStatus.CompletedWithErrors)
            return RetryEligibility.Ineligible($"Operation is in {operation.Status} state, must be CompletedWithErrors");

        var opInfo = operationDiscovery.GetOperation(operation.OperationName);
        if (opInfo is null)
            return RetryEligibility.Ineligible($"Operation type '{operation.OperationName}' not registered");

        if (!opInfo.IsRetryable)
            return RetryEligibility.Ineligible("Operation is not retryable (IsRetryable = false)");

        if (!opInfo.TrackRowData)
            return RetryEligibility.Ineligible("TrackRowData must be enabled for retry");

        var maxAttempts = options.Value.MaxRetryAttempts;
        if (maxAttempts > 0 && operation.RetryCount >= maxAttempts)
            return RetryEligibility.Ineligible($"Maximum retry attempts ({maxAttempts}) reached");

        var failedRows = await rowRecordRepository.QueryAsync(new BulkRowRecordQuery
        {
            OperationId = operationId,
            ErrorsOnly = true,
            PageSize = 1000
        }, cancellationToken).ConfigureAwait(false);

        var hasRetryableRows = failedRows.Items.Any(r => r.StepIndex >= 0);
        if (!hasRetryableRows)
            return RetryEligibility.Ineligible("No retryable failed rows found (validation failures cannot be retried)");

        return RetryEligibility.Eligible();
    }

    public async Task<RetrySubmission> RetryFailedRowsAsync(Guid operationId, RetryRequest request, CancellationToken cancellationToken = default)
    {
        var eligibility = await CanRetryAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (!eligibility.IsEligible)
            throw new InvalidOperationException($"Cannot retry operation: {eligibility.Reason}");

        var operation = (await operationRepository.GetByIdAsync(operationId, cancellationToken).ConfigureAwait(false))!;
        var opInfo = operationDiscovery.GetOperation(operation.OperationName)!;

        var query = new BulkRowRecordQuery
        {
            OperationId = operationId,
            ErrorsOnly = true,
            RowNumbers = request.RowNumbers,
            PageSize = 1000
        };

        var failedResult = await rowRecordRepository.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        var retryableRows = failedResult.Items.Where(r => r.StepIndex >= 0).ToList();

        var submitted = new List<BulkRowRecord>();
        var skippedReasons = new List<string>();

        foreach (var row in retryableRows)
        {
            if (opInfo.StepRetryability.TryGetValue(row.StepName, out var allowRetry) && !allowRetry)
            {
                skippedReasons.Add($"Row {row.RowNumber}: step '{row.StepName}' does not allow operation retry");
                continue;
            }

            submitted.Add(row);
        }

        if (submitted.Count == 0)
        {
            return new RetrySubmission
            {
                OperationId = operationId,
                RowsSubmitted = 0,
                RowsSkipped = skippedReasons.Count,
                SkippedReasons = skippedReasons.Count > 0 ? skippedReasons : null
            };
        }

        var historyRecords = submitted.Select(BulkRowRetryHistory.CreateFromRecord).ToList();
        await retryHistoryRepository.CreateBatchAsync(historyRecords, cancellationToken).ConfigureAwait(false);

        operation.MarkRetrying();
        await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);

        // Populate RowData from the validation record if missing on the step record.
        // Validation records (StepIndex=-1) carry serialized row data from the validation phase;
        // step records created by the executor do not.
        foreach (var row in submitted)
        {
            if (row.RowData is null)
            {
                var validationRecord = await rowRecordRepository
                    .GetByOperationRowStepAsync(operationId, row.RowNumber, -1, cancellationToken)
                    .ConfigureAwait(false);
                if (validationRecord?.RowData is not null)
                    row.RowData = validationRecord.RowData;
            }

            row.ResetForRetry(row.StepIndex);
        }

        await rowRecordRepository.UpdateBatchAsync(submitted, cancellationToken).ConfigureAwait(false);

        await scheduler.ScheduleBulkOperationAsync(operationId, cancellationToken).ConfigureAwait(false);

        logger.RetrySubmitted(operationId, submitted.Count);

        return new RetrySubmission
        {
            OperationId = operationId,
            RowsSubmitted = submitted.Count,
            RowsSkipped = skippedReasons.Count,
            SkippedReasons = skippedReasons.Count > 0 ? skippedReasons : null
        };
    }

    public Task<RetrySubmission> RetryRowAsync(Guid operationId, int rowNumber, CancellationToken cancellationToken = default)
    {
        return RetryFailedRowsAsync(operationId, new RetryRequest { RowNumbers = [rowNumber] }, cancellationToken);
    }
}
