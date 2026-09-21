using Avalonia.Controls;
using Avalonia.Input;
using FlightPathPlanner.Services;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class LookupTabView : UserControl
{
    public LookupTabView()
    {
        InitializeComponent();
    }

    private LookupTabViewModel? ViewModel => DataContext as LookupTabViewModel;

    private void QueryTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { CanSearch: true } vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }

    private void RadiusPreset_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: RadiusPresetOptionViewModel preset }) ViewModel?.SelectRadiusPreset(preset.Km);
    }

    private void Candidate_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: GeocodeResult candidate }) ViewModel?.SelectCandidate(candidate);
    }

    private async void ExportJsonButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await FileDialogs.SaveJsonAsync(this, "Export Flight Lookup as JSON", $"flight-lookup-{DateTime.UtcNow:yyyy-MM-dd}.json", vm.ExportMatchesToJson);
    }

    private async void ExportXlsxButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await FileDialogs.SaveXlsxAsync(this, "Export Flight Lookup as XLSX", $"flight-lookup-{DateTime.UtcNow:yyyy-MM-dd}.xlsx", vm.ExportMatchesToXlsxBytes);
    }
}
