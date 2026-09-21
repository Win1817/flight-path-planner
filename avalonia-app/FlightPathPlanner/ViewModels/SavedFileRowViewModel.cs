using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public partial class SavedFileRowViewModel(SavedFile file, bool canLoad) : ViewModelBase
{
    public SavedFile File { get; } = file;
    public bool IsArchived => File.IsArchived;
    public bool CanLoad { get; } = canLoad && !file.IsArchived;
    public string ArchiveButtonText => File.IsArchived ? "Restore" : "Archive";
    public string DeleteButtonText => ConfirmingDelete ? "Confirm delete" : "Delete";

    partial void OnConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(DeleteButtonText));

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool ConfirmingDelete { get; set; }

    public string DisplayName => File.DisplayName;

    public string MetaDisplay
    {
        get
        {
            var when = File.SavedAtUtc is { } t
                ? t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
                : "unknown date";
            return $"{when}  ·  {FormatSize(File.SizeBytes)}";
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
