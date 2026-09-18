using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class ReportTabView : UserControl
{
    public ReportTabView()
    {
        InitializeComponent();
    }

    private ReportTabViewModel? ViewModel => DataContext as ReportTabViewModel;

    private void AorOption_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: ReportAorOptionViewModel option } && ViewModel is { } vm)
        {
            vm.SelectedAor = option;
        }
    }

    private async void ExportJsonButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Geozone Report as JSON",
            SuggestedFileName = $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        if (file == null) return;

        var json = vm.ExportSummaryToJson();
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(json);
    }

    private async void ExportXlsxButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Geozone Report as XLSX",
            SuggestedFileName = $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } } },
        });
        if (file == null) return;

        var bytes = vm.ExportSummaryToXlsxBytes();
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
    }
}
