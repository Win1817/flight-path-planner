using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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

    private void RadiusPreset_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: RadiusPresetOptionViewModel preset })
        {
            ViewModel?.SelectRadiusPreset(preset.Km);
        }
    }

    private void Candidate_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: GeocodeResult candidate })
        {
            ViewModel?.SelectCandidate(candidate);
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
            Title = "Export Flight Lookup as JSON",
            SuggestedFileName = $"flight-lookup-{DateTime.UtcNow:yyyy-MM-dd}.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        if (file == null) return;

        var json = vm.ExportMatchesToJson();
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
            Title = "Export Flight Lookup as XLSX",
            SuggestedFileName = $"flight-lookup-{DateTime.UtcNow:yyyy-MM-dd}.xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } } },
        });
        if (file == null) return;

        var bytes = vm.ExportMatchesToXlsxBytes();
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
    }
}
