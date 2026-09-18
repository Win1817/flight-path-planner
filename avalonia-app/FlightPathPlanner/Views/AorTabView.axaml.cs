using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class AorTabView : UserControl
{
    public AorTabView()
    {
        InitializeComponent();
    }

    private AorTabViewModel? ViewModel => DataContext as AorTabViewModel;

    private async void UploadButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Upload AoR JSON",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file == null) return;

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        ViewModel?.LoadFromJson(text, file.Name);
    }

    private async void ExportJsonButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export AoRs as JSON",
            SuggestedFileName = $"aors-{DateTime.UtcNow:yyyy-MM-dd}.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        if (file == null) return;

        var json = vm.ExportSelectedToJson();
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
            Title = "Export AoRs as XLSX",
            SuggestedFileName = $"aors-{DateTime.UtcNow:yyyy-MM-dd}.xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } } },
        });
        if (file == null) return;

        var bytes = vm.ExportSelectedToXlsxBytes();
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
    }

    private void AorRow_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: AorRowViewModel row })
        {
            ViewModel?.ActivateAorCommand.Execute(row);
        }
    }

    private void DeleteAorButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: AorRowViewModel row })
        {
            ViewModel?.DeleteAorCommand.Execute(row);
        }
    }

    private void CloseDetailsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel?.CloseDetailsCommand.Execute(null);
    }
}
