namespace BulkSharp.Processing.Services;

internal sealed class BulkStepRecordManager(
    IBulkRowRecordRepository rowRecordRepository) : IBulkStepRecordManager
{
    public async Task<BulkRowRecord> CreateStepRecordAsync(Guid operationId, int rowNumber, string? rowId, string stepName, int stepIndex, CancellationToken ct = default)
    {
        var record = BulkRowRecord.CreateStep(operationId, rowNumber, rowId, stepName, stepIndex);
        await rowRecordRepository.CreateAsync(record, ct).ConfigureAwait(false);
        return record;
    }

    public async Task<BulkRowRecord?> GetStepRecordAsync(Guid operationId, int rowNumber, int stepIndex, CancellationToken ct = default)
    {
        return await rowRecordRepository.GetByOperationRowStepAsync(operationId, rowNumber, stepIndex, ct).ConfigureAwait(false);
    }

    public async Task MarkRunningAsync(BulkRowRecord record, CancellationToken ct = default)
    {
        record.MarkRunning();
        await rowRecordRepository.UpdateAsync(record, ct).ConfigureAwait(false);
    }

    public async Task MarkCompletedAsync(BulkRowRecord record, CancellationToken ct = default)
    {
        record.MarkCompleted();
        await rowRecordRepository.UpdateAsync(record, ct).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(BulkRowRecord record, string message, BulkErrorType errorType, CancellationToken ct = default)
    {
        record.MarkFailed(message, errorType);
        await rowRecordRepository.UpdateAsync(record, ct).ConfigureAwait(false);
    }

    public async Task MarkTimedOutAsync(BulkRowRecord record, string stepName, CancellationToken ct = default)
    {
        record.MarkTimedOut(stepName);
        await rowRecordRepository.UpdateAsync(record, ct).ConfigureAwait(false);
    }

    public async Task MarkWaitingForCompletionAsync(BulkRowRecord record, CancellationToken ct = default)
    {
        record.MarkWaitingForCompletion();
        await rowRecordRepository.UpdateAsync(record, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(BulkRowRecord record, CancellationToken ct = default)
    {
        await rowRecordRepository.UpdateAsync(record, ct).ConfigureAwait(false);
    }
}
