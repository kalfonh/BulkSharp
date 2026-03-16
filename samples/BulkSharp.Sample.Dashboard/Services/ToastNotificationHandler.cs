using BulkSharp.Core.Abstractions.Events;
using BulkSharp.Core.Domain.Events;
using BulkSharp.Dashboard.Services;

namespace BulkSharp.Sample.Dashboard.Services;

public class ToastNotificationHandler : IBulkOperationEventHandler
{
    private readonly ToastService _toastService;
    private readonly ILogger<ToastNotificationHandler> _logger;

    public ToastNotificationHandler(ToastService toastService, ILogger<ToastNotificationHandler> logger)
    {
        _toastService = toastService;
        _logger = logger;
    }

    public Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        var msg = $"{e.SuccessfulRows}/{e.TotalRows} rows succeeded in {e.Duration:g}";
        _toastService.Show(e.OperationName, msg, e.FailedRows > 0 ? ToastLevel.Warning : ToastLevel.Success);
        _logger.LogInformation("Operation '{Name}' completed: {Msg}", e.OperationName, msg);
        return Task.CompletedTask;
    }

    public Task OnOperationFailedAsync(BulkOperationFailedEvent e, CancellationToken ct)
    {
        _toastService.Show(e.OperationName, e.ErrorMessage, ToastLevel.Error);
        _logger.LogWarning("Operation '{Name}' failed: {Error}", e.OperationName, e.ErrorMessage);
        return Task.CompletedTask;
    }

    public Task OnStatusChangedAsync(BulkOperationStatusChangedEvent e, CancellationToken ct)
    {
        var msg = $"{e.PreviousStatus} -> {e.Status}";
        _toastService.Show(e.OperationName, msg, ToastLevel.Info);
        return Task.CompletedTask;
    }
}
