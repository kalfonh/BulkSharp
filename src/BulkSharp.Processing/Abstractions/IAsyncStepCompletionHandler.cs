namespace BulkSharp.Processing.Abstractions;

internal interface IAsyncStepCompletionHandler
{
    StepCompletionMode Mode { get; }

    /// <summary>
    /// Populates handler-specific fields on the record (e.g. SignalKey) before it is persisted.
    /// Called before WaitForCompletionAsync so external services can discover the record while waiting.
    /// </summary>
    void PrepareStatus<TMetadata, TRow>(
        IAsyncBulkStep<TMetadata, TRow> asyncStep,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        // Default: no-op (polling handler doesn't need to set anything)
    }

    Task WaitForCompletionAsync<TMetadata, TRow>(
        IAsyncBulkStep<TMetadata, TRow> asyncStep,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new();
}
