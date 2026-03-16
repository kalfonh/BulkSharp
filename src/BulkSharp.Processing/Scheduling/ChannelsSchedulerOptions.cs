namespace BulkSharp.Processing.Scheduling;

public sealed class ChannelsSchedulerOptions
{
    /// <summary>
    /// Number of concurrent workers processing operations
    /// </summary>
    public int WorkerCount { get; set; } = 4;

    /// <summary>
    /// Maximum queue capacity before backpressure is applied
    /// </summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>
    /// Behavior when queue is full
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;

    /// <summary>
    /// Maximum time to wait for graceful shutdown
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval at which the scheduler polls the database for new Pending operations.
    /// Null (default) disables polling — only startup recovery and explicit scheduling are used.
    /// Set this when running in a Worker-only process alongside an API that uses <c>AddBulkSharpApi()</c>.
    /// </summary>
    public TimeSpan? PendingPollInterval { get; set; }

    /// <summary>
    /// How long a Running operation must be stale before the scheduler marks it Failed and re-enqueues it.
    /// Protects against operations stuck in Running due to a previous process crash.
    /// Null (default) disables stuck-Running recovery.
    /// </summary>
    public TimeSpan? StuckOperationTimeout { get; set; }

    /// <summary>
    /// Validates that all option values are within acceptable ranges
    /// </summary>
    public void Validate()
    {
        if (WorkerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(WorkerCount), "WorkerCount must be greater than 0.");
        if (QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), "QueueCapacity must be greater than 0.");
        if (FullMode is BoundedChannelFullMode.DropWrite or BoundedChannelFullMode.DropOldest)
            throw new ArgumentException(
                $"FullMode '{FullMode}' is not supported — it would silently drop operations.",
                nameof(FullMode));
        if (PendingPollInterval.HasValue && PendingPollInterval.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PendingPollInterval), "PendingPollInterval must be greater than zero.");
        if (StuckOperationTimeout.HasValue && StuckOperationTimeout.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StuckOperationTimeout), "StuckOperationTimeout must be greater than zero.");
    }
}