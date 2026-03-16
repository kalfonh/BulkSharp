namespace BulkSharp.Processing.Abstractions;

/// <summary>Executes individual steps with retry and exponential backoff.</summary>
internal interface IBulkStepExecutor
{
    /// <summary>Executes a step with retry logic only (no status tracking).</summary>
    Task ExecuteStepAsync<TMetadata, TRow>(
        IBulkStep<TMetadata, TRow> step,
        TRow row,
        TMetadata metadata,
        CancellationToken cancellationToken = default)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new();

    /// <summary>
    /// Executes a step with an existing record for status tracking.
    /// For <see cref="IAsyncBulkStep{TMetadata, TRow}"/> steps, handles polling or signal-based completion.
    /// </summary>
    Task ExecuteStepAsync<TMetadata, TRow>(
        IBulkStep<TMetadata, TRow> step,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record,
        IBulkStepRecordManager recordManager,
        CancellationToken cancellationToken = default)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new();
}
