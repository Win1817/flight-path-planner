using Avalonia.Controls;
using Avalonia.Threading;
using FlightPathPlanner.Services;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    // One short pause absorbs bursts (typing in a filter, a batch of selection changes, a pan) into a single map refresh.
    private readonly DispatcherTimer _mapTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _highlightTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _fitPending = true;
    private int _mapVersion;
    private CancellationTokenSource? _mapCts;

    public MainWindow()
    {
        InitializeComponent();

        _mapTimer.Tick += (_, _) =>
        {
            _mapTimer.Stop();
            RefreshMap();
        };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            if (_vm != null) Map.UpdateHighlights(_vm.BuildHighlightIds());
        };

        Map.ZoneClicked += (id, type) => _vm?.HandleMapZoneClicked(id, type);
        Map.ZoneHovered += id => _vm?.HandleMapZoneHovered(id);
        Map.ViewportChanged += (w, s, e, n) => _vm?.HandleMapViewportChanged(w, s, e, n);

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.MapDataInvalidated -= OnMapDataInvalidated;
                _vm.MapSelectionChanged -= RestartMapTimer;
                _vm.MapViewportChanged -= RestartMapTimer;
                _vm.MapHighlightsInvalidated -= OnMapHighlightsInvalidated;
            }
            _vm = DataContext as MainViewModel;
            if (_vm == null) return;
            _vm.MapDataInvalidated += OnMapDataInvalidated;
            _vm.MapSelectionChanged += RestartMapTimer;
            _vm.MapViewportChanged += RestartMapTimer;
            _vm.MapHighlightsInvalidated += OnMapHighlightsInvalidated;
        };
    }

    /// <summary>Builds the map update on a worker thread from an immutable snapshot, then hands it to the page on the UI thread.
    /// A newer request cancels the one in flight, so fast input never queues stale work.</summary>
    private async void RefreshMap()
    {
        var vm = _vm;
        if (vm == null) return;

        _mapCts?.Cancel();
        var cts = _mapCts = new CancellationTokenSource();
        int version = ++_mapVersion;
        bool fit = _fitPending;
        _fitPending = false;

        var build = vm.CreateMapRequest(fit, version); // UI thread: cheap snapshot

        MapPayload payload;
        try
        {
            payload = await Task.Run(() => build(cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (fit) _fitPending = true; // the camera move was never delivered; the next request must still fit
            return;
        }
        catch (Exception ex)
        {
            AppLog.Diagnostic($"map build failed: {ex.Message}");
            if (fit) _fitPending = true;
            return;
        }

        if (version != _mapVersion)
        {
            if (fit) _fitPending = true;
            return;
        }

        Map.PushPayload(payload);
        Map.UpdateHighlights(vm.BuildHighlightIds());

        if (payload.ViewportMode)
            AppLog.Diagnostic($"map v{payload.Version}: {payload.Shown:N0} drawn / {payload.InView:N0} in view / {payload.Total:N0} total, {payload.Chunks.Count} chunk(s) / {payload.Chunks.Sum(c => (long)c.Length) / 1048576.0:0.0} MB, built in {payload.BuildTime.TotalMilliseconds:0} ms");
    }

    private void OnMapDataInvalidated()
    {
        _fitPending = true;
        RestartMapTimer();
    }

    private void RestartMapTimer()
    {
        _mapTimer.Stop();
        _mapTimer.Start();
    }

    private void OnMapHighlightsInvalidated()
    {
        if (_mapTimer.IsEnabled) return; // the pending map refresh also re-sends the highlights
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void DismissToast_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ToastViewModel toast) _vm?.Toasts.Dismiss(toast);
    }
}
