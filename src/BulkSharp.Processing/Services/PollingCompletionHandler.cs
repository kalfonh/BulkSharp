using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class PollingCompletionHandler(ILogger<PollingCompletionHandler> logger) : IAsyncStepCompletionHandler
{
    public StepCompletionMode Mode => StepCompletionMode.Polling;

    public async Task WaitForCompletionAsync<TMetadata, TRow>(
        IAsyncBulkStep<TMetadata, TRow> asyncStep,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        var stopwatch = Stopwatch.StartNew();
        var timeoutMs = asyncStep.Timeout.TotalMilliseconds;
        var pollIntervalMs = asyncStep.PollInterval.TotalMilliseconds;

        logger.PollingStepStarted(asyncStep.Name, asyncStep.PollInterval.TotalSeconds, asyncStep.Timeout.TotalSeconds);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await asyncStep.CheckCompletionAsync(row, metadata, cancellationToken).ConfigureAwait(false))
            {
                logger.PollingStepCompleted(asyncStep.Name);
                return;
            }

            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            var remainingMs = timeoutMs - elapsedMs;

            if (remainingMs <= 0)
                break;

            var delayMs = Math.Min(pollIntervalMs, remainingMs);
            var jitter = Random.Shared.Next(0, (int)(pollIntervalMs * 0.2));
            delayMs = Math.Min(delayMs + jitter, remainingMs);

            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }

        throw new BulkStepTimeoutException(asyncStep.Name, asyncStep.Timeout);
    }
}
