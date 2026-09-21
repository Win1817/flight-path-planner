using Avalonia.Controls;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class ReportTabView : UserControl
{
    public ReportTabView()
    {
        InitializeComponent();
    }

    private ReportTabViewModel? ViewModel => DataContext as ReportTabViewModel;

    // Only *additions* are user picks; removals happen when the search filter hides the chosen AoR.
    private void AorList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ReportAorOptionViewModel option && ViewModel is { } vm && !ReferenceEquals(vm.SelectedAor, option))
            vm.SelectedAor = option;
    }

    private async void ExportJsonButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await FileDialogs.SaveJsonAsync(this, "Export Geozone Report as JSON", $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.json", vm.ExportSummaryToJson);
    }

    private async void ExportXlsxButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await FileDialogs.SaveXlsxAsync(this, "Export Geozone Report as XLSX", $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.xlsx", vm.ExportSummaryToXlsxBytes);
    }
}
