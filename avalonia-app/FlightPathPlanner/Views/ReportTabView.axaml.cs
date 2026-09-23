using Avalonia.Controls;
using Avalonia.Input;
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
        if (ViewModel is not { } vm) return;
        var export = vm.PrepareSummaryExport();
        if (await FileDialogs.SaveJsonAsync(this, "Export Geozone Report as JSON", $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.json", () => export.BuildJson))
            await vm.SaveLocalCopyAsync(export);
    }

    private async void ExportXlsxButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var export = vm.PrepareSummaryExport();
        if (await FileDialogs.SaveXlsxAsync(this, "Export Geozone Report as XLSX", $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.xlsx", () => export.BuildXlsx))
            await vm.SaveLocalCopyAsync(export);
    }

    private void OpRow_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: OpsRowViewModel row }) ViewModel?.ActivateOpCommand.Execute(row);
    }

    private void CloseDetailsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel?.CloseDetailsCommand.Execute(null);
    }
}
