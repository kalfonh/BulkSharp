using BulkSharp.Processing.Logging;

namespace BulkSharp.Processing.Services;

internal sealed class SignalCompletionHandler(
    IBulkStepSignalRegistry signalRegistry,
    IBulkRowRecordRepository recordRepository,
    ILogger<SignalCompletionHandler> logger) : IAsyncStepCompletionHandler
{
    public StepCompletionMode Mode => StepCompletionMode.Signal;

    public void PrepareStatus<TMetadata, TRow>(
        IAsyncBulkStep<TMetadata, TRow> asyncStep,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        var userKey = asyncStep.GetSignalKey(row, metadata);
        record.SignalKey = $"{record.BulkOperationId}:{userKey}:{record.RowNumber}";
    }

    public async Task WaitForCompletionAsync<TMetadata, TRow>(
        IAsyncBulkStep<TMetadata, TRow> asyncStep,
        TRow row,
        TMetadata metadata,
        BulkRowRecord record,
        CancellationToken cancellationToken)
        where TMetadata : IBulkMetadata, new()
        where TRow : class, IBulkRow, new()
    {
        var signalKey = record.SignalKey
            ?? throw new InvalidOperationException("SignalKey must be set by PrepareStatus before waiting.");

        var tcs = signalRegistry.GetOrRegisterWaiter(signalKey);

        logger.SignalStepWaiting(asyncStep.Name, signalKey, asyncStep.Timeout.TotalSeconds);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(asyncStep.Timeout);

            // Wait for either in-process signal or DB state change (cross-process)
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
            var pollTask = PollDbForCompletionAsync(record, pollCts.Token);
            var signalTask = tcs.Task;

            var winner = await Task.WhenAny(signalTask, pollTask).ConfigureAwait(false);

            if (winner == pollTask)
            {
                // DB showed completion (cross-process signal). Cancel the in-process waiter.
                signalRegistry.RemoveWaiter(signalKey);
                var dbResult = await pollTask.ConfigureAwait(false);
                if (!dbResult)
                    throw new BulkStepTimeoutException(asyncStep.Name, asyncStep.Timeout);
            }
            else
            {
                // In-process signal arrived. Cancel the poll.
                await pollCts.CancelAsync().ConfigureAwait(false);
                await signalTask.ConfigureAwait(false); // propagate exceptions (BulkStepSignalFailureException)
            }

            logger.SignalStepReceived(asyncStep.Name, signalKey);
        }
        catch (BulkStepSignalFailureException ex)
        {
            logger.SignalStepFailed(asyncStep.Name, signalKey, ex.Message);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            signalRegistry.RemoveWaiter(signalKey);
            throw new BulkStepTimeoutException(asyncStep.Name, asyncStep.Timeout);
        }
        catch (OperationCanceledException)
        {
            signalRegistry.RemoveWaiter(signalKey);
            throw;
        }
    }

    private async Task<bool> PollDbForCompletionAsync(BulkRowRecord record, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

            var current = await recordRepository.GetBySignalKeyAsync(record.SignalKey!, cancellationToken).ConfigureAwait(false);
            if (current == null)
                continue;

            if (current.State == RowRecordState.Completed)
                return true;

            if (current.State == RowRecordState.Failed)
                throw new BulkStepSignalFailureException(record.SignalKey!, current.ErrorMessage ?? "Signal failed (cross-process)");
        }

        return false;
    }
}
