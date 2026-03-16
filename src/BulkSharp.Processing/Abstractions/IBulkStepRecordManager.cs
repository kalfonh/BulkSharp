namespace BulkSharp.Processing.Abstractions;

/// <summary>Manages the lifecycle of <see cref="BulkRowRecord"/> instances for step execution.</summary>
internal interface IBulkStepRecordManager
{
    /// <summary>Creates a new step record for initial processing.</summary>
    Task<BulkRowRecord> CreateStepRecordAsync(Guid operationId, int rowNumber, string? rowId, string stepName, int stepIndex, CancellationToken ct = default);

    /// <summary>Loads an existing step record for retry processing.</summary>
    Task<BulkRowRecord?> GetStepRecordAsync(Guid operationId, int rowNumber, int stepIndex, CancellationToken ct = default);

    Task MarkRunningAsync(BulkRowRecord record, CancellationToken ct = default);
    Task MarkCompletedAsync(BulkRowRecord record, CancellationToken ct = default);
    Task MarkFailedAsync(BulkRowRecord record, string message, BulkErrorType errorType, CancellationToken ct = default);
    Task MarkTimedOutAsync(BulkRowRecord record, string stepName, CancellationToken ct = default);
    Task MarkWaitingForCompletionAsync(BulkRowRecord record, CancellationToken ct = default);
    Task UpdateAsync(BulkRowRecord record, CancellationToken ct = default);
}
