namespace BulkSharp.Core.Configuration;

/// <summary>
/// Global configuration options for BulkSharp.
/// </summary>
public sealed class BulkSharpOptions
{
    /// <summary>
    /// Maximum allowed file size in bytes. Default: 100 MB.
    /// Set to 0 to disable the limit.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Whether to include serialized row data in error records.
    /// WARNING: May contain PII. Disable in production if rows contain sensitive data.
    /// Default: false.
    /// </summary>
    public bool IncludeRowDataInErrors { get; set; }

    /// <summary>
    /// Number of rows between progress flushes (error batch writes + status updates).
    /// Default: 100.
    /// </summary>
    public int FlushBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of rows to process concurrently.
    /// Default: 1 (sequential processing, backward compatible).
    /// Set higher for parallel row processing — useful when operations have async steps.
    /// </summary>
    public int MaxRowConcurrency { get; set; } = 1;

    /// <summary>
    /// Enables the orphaned step recovery hosted service that runs on startup to
    /// transition rows stuck in WaitingForCompletion to Failed after an application restart.
    /// Only needed when using signal-based async steps. Default: false.
    /// </summary>
    public bool EnableOrphanedStepRecovery { get; set; }

    /// <summary>
    /// Identifies this service instance in a multi-service architecture.
    /// Stored on each created operation as <see cref="BulkOperation.Source"/>
    /// so the gateway can route requests to the correct backend.
    /// Null means unset (single-service deployment).
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Validates that all option values are within acceptable ranges.
    /// Called automatically during service registration.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="FlushBatchSize"/> is less than or equal to zero,
    /// or when <see cref="MaxFileSizeBytes"/> is negative,
    /// or when <see cref="MaxRowConcurrency"/> is less than or equal to zero.
    /// </exception>
    internal void Validate()
    {
        if (FlushBatchSize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(FlushBatchSize),
                FlushBatchSize,
                "FlushBatchSize must be greater than zero.");

        if (MaxFileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaxFileSizeBytes),
                MaxFileSizeBytes,
                "MaxFileSizeBytes must be zero (disabled) or a positive value.");

        if (MaxRowConcurrency <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaxRowConcurrency),
                MaxRowConcurrency,
                "MaxRowConcurrency must be greater than zero.");
    }
}
