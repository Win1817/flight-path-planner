using Avalonia.Controls;
using Avalonia.Threading;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private readonly DispatcherTimer _dataTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _highlightTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public MainWindow()
    {
        InitializeComponent();

        // Debounce: typing in a filter box rebuilds the shape set on every keystroke.
        _dataTimer.Tick += (_, _) =>
        {
            _dataTimer.Stop();
            if (_vm == null) return;
            Map.UpdateData(_vm.BuildMapGeoJson());
            Map.UpdateHighlights(_vm.BuildHighlightIds());
        };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            if (_vm != null) Map.UpdateHighlights(_vm.BuildHighlightIds());
        };

        Map.ZoneClicked += (id, type) => _vm?.HandleMapZoneClicked(id, type);
        Map.ZoneHovered += id => _vm?.HandleMapZoneHovered(id);

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.MapDataInvalidated -= OnMapDataInvalidated;
                _vm.MapHighlightsInvalidated -= OnMapHighlightsInvalidated;
            }
            _vm = DataContext as MainViewModel;
            if (_vm == null) return;
            _vm.MapDataInvalidated += OnMapDataInvalidated;
            _vm.MapHighlightsInvalidated += OnMapHighlightsInvalidated;
        };
    }

    private void DismissToast_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ToastViewModel toast) _vm?.Toasts.Dismiss(toast);
    }

    private void OnMapDataInvalidated()
    {
        _dataTimer.Stop();
        _dataTimer.Start();
    }

    private void OnMapHighlightsInvalidated()
    {
        if (_dataTimer.IsEnabled) return; // the pending data push also applies highlights
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }
}
