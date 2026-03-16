using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;

namespace BulkSharp.Sample.Dashboard.Services;

/// <summary>
/// Simulates carrier webhook callbacks by auto-signaling shipment steps after a delay.
/// This is ONLY for the demo -- not part of the BulkSharp library.
/// </summary>
public class DemoSignalService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DemoSignalService> _logger;

    public DemoSignalService(IServiceProvider serviceProvider, ILogger<DemoSignalService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var rowRecordRepo = scope.ServiceProvider.GetRequiredService<IBulkRowRecordRepository>();
                var signalService = scope.ServiceProvider.GetRequiredService<IBulkStepSignalService>();
                var operationRepo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();

                var runningOps = await operationRepo.QueryAsync(
                    new BulkOperationQuery { Status = BulkOperationStatus.Running, PageSize = 100 }, stoppingToken);

                foreach (var op in runningOps.Items)
                {
                    var waitingRecords = await rowRecordRepo.QueryAsync(
                        new BulkRowRecordQuery
                        {
                            OperationId = op.Id,
                            State = RowRecordState.WaitingForCompletion
                        }, stoppingToken);

                    foreach (var record in waitingRecords.Items)
                    {
                        if (record.SignalKey is null)
                            continue;

                        // Only signal if the step has been waiting for at least 5 seconds
                        if (DateTime.UtcNow - record.StartedAt < TimeSpan.FromSeconds(5))
                            continue;

                        if (signalService.TrySignal(record.SignalKey))
                        {
                            _logger.LogInformation(
                                "Demo: Auto-signaled shipment completion for key '{SignalKey}' (row {RowNumber})",
                                record.SignalKey, record.RowNumber);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Demo signal service error -- will retry");
            }
        }
    }
}
