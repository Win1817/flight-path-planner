namespace FlightPathPlanner.Services;

public enum NotificationKind { Success, Info, Warning, Error }

/// <summary>Fire-and-forget user feedback. View models and views post here; the main window's toast queue listens.
/// Kept static so business code needs no UI dependency and tests need no setup (unobserved posts are dropped).</summary>
public static class Notifier
{
    public static event Action<NotificationKind, string>? Posted;

    public static void Post(NotificationKind kind, string message) => Posted?.Invoke(kind, message);
    public static void Success(string message) => Post(NotificationKind.Success, message);
    public static void Info(string message) => Post(NotificationKind.Info, message);
    public static void Warning(string message) => Post(NotificationKind.Warning, message);
    public static void Error(string message) => Post(NotificationKind.Error, message);
}
