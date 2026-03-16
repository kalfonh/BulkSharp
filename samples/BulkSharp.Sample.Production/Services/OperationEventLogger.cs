using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Dashboard.Services;

namespace BulkSharp.Sample.Production.Services;

public class OperationEventLogger : IBulkOperationEventHandler
{
    private readonly ILogger<OperationEventLogger> _logger;
    private readonly ToastService _toastService;

    public OperationEventLogger(ILogger<OperationEventLogger> logger, ToastService toastService)
    {
        _logger = logger;
        _toastService = toastService;
    }

    public Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        var msg = $"{e.SuccessfulRows}/{e.TotalRows} rows succeeded in {e.Duration:g}";
        _logger.LogInformation(
            "Operation '{OperationName}' completed: {Message}",
            e.OperationName, msg);
        _toastService.Show(e.OperationName, msg, e.FailedRows > 0 ? ToastLevel.Warning : ToastLevel.Success);
        return Task.CompletedTask;
    }

    public Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct)
    {
        _logger.LogWarning(
            "Operation '{OperationName}' failed: {ErrorMessage}",
            e.OperationName, e.ErrorMessage);
        _toastService.Show(e.OperationName, e.ErrorMessage, ToastLevel.Error);
        return Task.CompletedTask;
    }

    public Task OnStatusChangedAsync(BulkOperationStatusChangedEvent e, CancellationToken ct)
    {
        var msg = $"{e.PreviousStatus} -> {e.Status}";
        _logger.LogInformation(
            "Operation '{OperationName}' status: {Message}",
            e.OperationName, msg);
        _toastService.Show(e.OperationName, msg, ToastLevel.Info);
        return Task.CompletedTask;
    }
}
