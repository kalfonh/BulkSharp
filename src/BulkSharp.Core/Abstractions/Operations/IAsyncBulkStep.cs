namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>
/// A bulk step that starts work asynchronously and completes via polling or external signal.
/// ExecuteAsync initiates the work. Completion is determined by CompletionMode.
/// </summary>
public interface IAsyncBulkStep<TMetadata, TRow> : IBulkStep<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    /// <summary>How this step determines completion after ExecuteAsync returns.</summary>
    StepCompletionMode CompletionMode { get; }

    /// <summary>For Polling mode: interval between completion checks.</summary>
    TimeSpan PollInterval { get; }

    /// <summary>Maximum time to wait for completion before timing out.</summary>
    TimeSpan Timeout { get; }

    /// <summary>
    /// For Polling mode: check whether the step has completed for this row.
    /// Called repeatedly at PollInterval until it returns true or Timeout is reached.
    /// </summary>
    Task<bool> CheckCompletionAsync(TRow row, TMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// For Signal mode: returns a domain-meaningful key identifying this row's signal.
    /// The framework automatically scopes the key by prefixing the operation ID,
    /// so the same row-level key (e.g. "shipment-ORD-001") can safely appear in
    /// concurrent operations without collision.
    /// External callers signal via POST /api/bulks/{id}/signal/{key} using the
    /// unscoped key returned here; the endpoint composes the full scoped key internally.
    /// </summary>
    string GetSignalKey(TRow row, TMetadata metadata);
}
