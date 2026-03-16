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
    /// Executes a step with retry logic, per-row status tracking, and async step completion handling.
    /// For <see cref="IAsyncBulkStep{TMetadata, TRow}"/> steps, handles polling or signal-based completion.
    /// </summary>
    Task ExecuteStepAsync<TMetadata, TRow>(
        IBulkStep<TMetadata, TRow> step,
        TRow row,
        TMetadata metadata,
        Guid operationId,
        int rowNumber,
        int stepIndex,
        CancellationToken cancellationToken = default)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new();
}
