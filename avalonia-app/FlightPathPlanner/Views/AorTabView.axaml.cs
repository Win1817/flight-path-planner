using Avalonia.Controls;
using Avalonia.Input;
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
        var vm = ViewModel;
        if (vm == null || !vm.Import.CanStart) return;
        if (await FileDialogs.PickJsonSourceAsync(this, "Upload AoR JSON") is not { } source) return;

        // The file is streamed and parsed on a worker thread; the window stays responsive and the import can be cancelled.
        await vm.RequestImportAsync(source);
    }

    private async void ExportJsonButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await FileDialogs.SaveJsonAsync(this, "Export AoRs as JSON", $"aors-{DateTime.UtcNow:yyyy-MM-dd}.json", () => vm.PrepareSelectedJsonExport());
    }

    private async void ExportXlsxButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await FileDialogs.SaveXlsxAsync(this, "Export AoRs as XLSX", $"aors-{DateTime.UtcNow:yyyy-MM-dd}.xlsx", () => vm.PrepareSelectedXlsxExport());
    }

    private void AorRow_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: AorRowViewModel row }) ViewModel?.ActivateAorCommand.Execute(row);
    }

    private void DeleteAorButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: AorRowViewModel row }) ViewModel?.DeleteAorCommand.Execute(row);
    }

    // Keep room for the list: the inspector never takes more than about a third of the tab's height.
    private void Root_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        InspectorCard.MaxHeight = Math.Clamp(e.NewSize.Height * 0.34, 150, 340);
    }

    private void CloseDetailsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel?.CloseDetailsCommand.Execute(null);
    }
}
