namespace BulkSharp.Dashboard.Services;

public sealed class ToastService
{
    private readonly List<ToastMessage> _messages = new();
    private readonly object _lock = new();

    public event Action? OnChange;

    public IReadOnlyList<ToastMessage> Messages
    {
        get { lock (_lock) return _messages.ToList(); }
    }

    public void Show(string title, string message, ToastLevel level = ToastLevel.Info)
    {
        var toast = new ToastMessage(Guid.NewGuid(), title, message, level, DateTime.UtcNow);
        lock (_lock) _messages.Add(toast);
        OnChange?.Invoke();
        _ = Task.Delay(8000).ContinueWith(_ => Dismiss(toast.Id));
    }

    public void Dismiss(Guid id)
    {
        lock (_lock) _messages.RemoveAll(m => m.Id == id);
        OnChange?.Invoke();
    }
}

public record ToastMessage(Guid Id, string Title, string Message, ToastLevel Level, DateTime Timestamp);

public enum ToastLevel { Info, Success, Warning, Error }
