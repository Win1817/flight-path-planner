using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

/// <summary>Compact, self-dismissing notifications (newest last, at most <see cref="MaxVisible"/> at once).</summary>
public partial class ToastCenterViewModel : ViewModelBase
{
    public const int MaxVisible = 3;

    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <param name="listenToNotifier">Subscribe to the global <see cref="Notifier"/>. Tests that need an isolated queue pass false.</param>
    public ToastCenterViewModel(bool listenToNotifier = true)
    {
        if (listenToNotifier) Notifier.Posted += Show;
    }

    public void Show(NotificationKind kind, string message)
    {
        var toast = new ToastViewModel(kind, message);
        Toasts.Add(toast);
        while (Toasts.Count > MaxVisible) Toasts.RemoveAt(0);

        // Errors stay longer so they can be read.
        var lifetime = kind == NotificationKind.Error ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(4);
        _ = Task.Delay(lifetime).ContinueWith(_ => Dispatcher.UIThread.Post(() => Toasts.Remove(toast)));
    }

    public void Dismiss(ToastViewModel toast) => Toasts.Remove(toast);
}
