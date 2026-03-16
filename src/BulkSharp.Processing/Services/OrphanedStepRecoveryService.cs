using BulkSharp.Core.Domain.Queries;
using BulkSharp.Processing.Logging;
using Microsoft.Extensions.Hosting;

namespace BulkSharp.Processing.Services;

/// <summary>
/// Hosted service that recovers rows stuck in WaitingForCompletion state after an application restart.
/// Signal-based async steps register in-process waiters that are lost on restart. This service
/// transitions orphaned rows to Failed state so they are not permanently stuck.
/// Also detects Running operations that have no WaitingForCompletion rows but are older than
/// the age threshold — these are stuck operations that should be marked as failed.
/// </summary>
internal sealed class OrphanedStepRecoveryService(
    IBulkRowRecordRepository rowRecordRepository,
    IBulkOperationRepository operationRepository,
    IOptions<BulkSharpOptions> options,
    ILogger<OrphanedStepRecoveryService> logger) : BackgroundService
{
    private const int PageSize = 1000;
    private static readonly TimeSpan AgeThreshold = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.EnableOrphanedStepRecovery)
        {
            logger.OrphanedStepRecoveryDisabled();
            return;
        }

        logger.CheckingForOrphanedSteps();

        try
        {
            var cutoff = DateTime.UtcNow - AgeThreshold;
            var recoveredCount = 0;
            HashSet<Guid> operationsWithOrphanedSteps = [];

            // Pass 1: Recover rows stuck in WaitingForCompletion
            int operationPage = 1;
            PagedResult<BulkOperation> operationResult;
            do
            {
                var operationQuery = new BulkOperationQuery
                {
                    Status = BulkOperationStatus.Running,
                    PageSize = PageSize,
                    Page = operationPage
                };

                operationResult = await operationRepository.QueryAsync(operationQuery, cancellationToken).ConfigureAwait(false);

                foreach (var operation in operationResult.Items)
                {
                    var operationRecovered = await RecoverOrphanedStepsAsync(
                        operation, cutoff, cancellationToken).ConfigureAwait(false);

                    recoveredCount += operationRecovered;

                    if (operationRecovered > 0)
                    {
                        operationsWithOrphanedSteps.Add(operation.Id);
                        operation.MarkFailed("Operation had orphaned signal-based steps on application restart");
                        await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
                        logger.RecoveredOrphanedSteps(operationRecovered, operation.Id);
                    }
                }

                operationPage++;
            } while (operationResult.HasNextPage);

            // Pass 2: Recover stuck Running operations with no WaitingForCompletion rows
            // that are older than the age threshold
            operationPage = 1;
            do
            {
                var operationQuery = new BulkOperationQuery
                {
                    Status = BulkOperationStatus.Running,
                    PageSize = PageSize,
                    Page = operationPage
                };

                operationResult = await operationRepository.QueryAsync(operationQuery, cancellationToken).ConfigureAwait(false);

                foreach (var operation in operationResult.Items)
                {
                    // Skip operations already handled in pass 1
                    if (operationsWithOrphanedSteps.Contains(operation.Id))
                        continue;

                    // Only recover operations older than the age threshold
                    if (operation.StartedAt == null || operation.StartedAt >= cutoff)
                        continue;

                    // Check if this operation has any WaitingForCompletion rows
                    var waitingQuery = new BulkRowRecordQuery
                    {
                        OperationId = operation.Id,
                        State = RowRecordState.WaitingForCompletion,
                        PageSize = 1,
                        Page = 1
                    };
                    var waitingResult = await rowRecordRepository.QueryAsync(waitingQuery, cancellationToken).ConfigureAwait(false);

                    if (waitingResult.TotalCount == 0)
                    {
                        operation.MarkFailed("Operation was stuck in Running state after application restart");
                        await operationRepository.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
                        logger.MarkedStuckOperationFailed(operation.Id, operation.StartedAt);
                    }
                }

                operationPage++;
            } while (operationResult.HasNextPage);

            if (recoveredCount > 0)
                logger.OrphanedStepRecoveryComplete(recoveredCount);
            else
                logger.NoOrphanedStepsFound();
        }
        catch (Exception ex) when (IsDbNotReady(ex))
        {
            logger.OrphanedStepRecoveryDbNotReady();
        }
        catch (Exception ex)
        {
            logger.OrphanedStepRecoveryFailed(ex);
        }
    }

    /// <summary>
    /// Recovers orphaned WaitingForCompletion row records for a single operation,
    /// using pagination and batch updates.
    /// </summary>
    private async Task<int> RecoverOrphanedStepsAsync(
        BulkOperation operation,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var recoveredCount = 0;
        int page = 1;
        PagedResult<BulkRowRecord> result;

        do
        {
            var query = new BulkRowRecordQuery
            {
                OperationId = operation.Id,
                State = RowRecordState.WaitingForCompletion,
                PageSize = PageSize,
                Page = page
            };

            result = await rowRecordRepository.QueryAsync(query, cancellationToken).ConfigureAwait(false);

            List<BulkRowRecord> toUpdate = [];
            foreach (var record in result.Items)
            {
                // Only recover rows older than the age threshold
                if (record.StartedAt >= cutoff)
                    continue;

                record.MarkFailed("Recovery: step was waiting for signal when application restarted", BulkErrorType.SignalFailure);
                toUpdate.Add(record);
            }

            if (toUpdate.Count > 0)
            {
                await rowRecordRepository.UpdateBatchAsync(toUpdate, cancellationToken).ConfigureAwait(false);
                recoveredCount += toUpdate.Count;
            }

            page++;
        } while (result.HasNextPage);

        return recoveredCount;
    }

    /// <summary>
    /// Detects database-not-ready conditions (missing tables, connection failures) that are
    /// expected on first startup before migrations have run.
    /// </summary>
    private static bool IsDbNotReady(Exception ex)
    {
        var fullMessage = ex.ToString();

        if (fullMessage.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fullMessage.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fullMessage.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
            fullMessage.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fullMessage.Contains("A network-related", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
