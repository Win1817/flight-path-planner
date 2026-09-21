using Avalonia.Controls;
using Avalonia.Interactivity;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Views;

public partial class SavedFilesPanel : UserControl
{
    public SavedFilesPanel()
    {
        InitializeComponent();
    }

    private SavedFilesViewModel? ViewModel => DataContext as SavedFilesViewModel;

    private static SavedFileRowViewModel? RowOf(object? sender) => (sender as Control)?.Tag as SavedFileRowViewModel;

    private async void Load_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && ViewModel is { } vm) await vm.LoadAsync(row);
    }

    private void ToggleArchive_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel?.ToggleArchive(row);
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel?.RequestDelete(row);
    }

    private void CancelDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) ViewModel?.CancelDelete(row);
    }
}
