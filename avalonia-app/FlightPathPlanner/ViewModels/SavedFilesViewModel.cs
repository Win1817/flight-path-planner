using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

/// <summary>The "Saved locally" panel for one category: lists files kept in the portable data folder, and lets the user
/// reload, archive/restore and delete them (singly or in bulk). Mirrors the web app's SavedFilesPanel.</summary>
public partial class SavedFilesViewModel : ViewModelBase
{
    private readonly LocalStorageService? _storage;
    private readonly StorageCategory _category;
    private readonly Func<string, string, bool>? _loader;

    /// <param name="loader">Called with (json text, original file name) when the user reloads a file; returns whether it
    /// loaded. Null for categories that can't be reloaded (reports).</param>
    public SavedFilesViewModel(LocalStorageService? storage, StorageCategory category, Func<string, string, bool>? loader = null)
    {
        _storage = storage;
        _category = category;
        _loader = loader;
        Refresh();
    }

    [ObservableProperty]
    public partial ObservableCollection<SavedFileRowViewModel> Rows { get; set; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool ConfirmingBulkDelete { get; set; }

    public int ActiveCount { get; private set; }
    public int ArchivedCount { get; private set; }

    public bool IsAvailable => _storage != null;
    public bool CanLoad => _loader != null;
    public bool HasRows => Rows.Count > 0;
    public bool HasNoRows => !HasRows;
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string HeaderText => $"Saved locally ({ActiveCount})";
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public string ArchiveToggleText => ShowArchived ? $"Back to saved ({ActiveCount})" : $"Archive ({ArchivedCount})";
    public string ArchiveSelectedText => ShowArchived ? "Restore selected" : "Archive selected";
    public string EmptyText => ShowArchived ? "Nothing archived." : "Nothing saved yet — uploads are saved automatically.";
    public string DeleteSelectedText => ConfirmingBulkDelete ? $"Confirm delete ({SelectedCount})" : "Delete selected";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));
    partial void OnShowArchivedChanged(bool value) => Refresh();
    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnConfirmingBulkDeleteChanged(bool value) => OnPropertyChanged(nameof(DeleteSelectedText));

    public void Refresh()
    {
        if (_storage == null) return;

        try
        {
            var active = _storage.List(_category, archived: false);
            var archived = _storage.List(_category, archived: true);
            ActiveCount = active.Count;
            ArchivedCount = archived.Count;

            var rows = new ObservableCollection<SavedFileRowViewModel>();
            foreach (var file in ShowArchived ? archived : active)
            {
                var row = new SavedFileRowViewModel(file, CanLoad);
                row.PropertyChanged += OnRowPropertyChanged;
                rows.Add(row);
            }
            Rows = rows;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read the data folder: {ex.Message}";
        }

        ConfirmingBulkDelete = false;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasNoRows));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ArchiveToggleText));
        OnPropertyChanged(nameof(ArchiveSelectedText));
        OnPropertyChanged(nameof(EmptyText));
        RaiseSelectionChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SavedFileRowViewModel.IsSelected))
        {
            ConfirmingBulkDelete = false;
            RaiseSelectionChanged();
        }
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DeleteSelectedText));
    }

    /// <summary>Saves newly uploaded/exported content. Failures are shown in the panel instead of interrupting the user.</summary>
    public void SaveNew(string originalName, string content)
    {
        if (_storage == null) return;
        try
        {
            _storage.Save(_category, originalName, content);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save a local copy: {ex.Message}";
        }
        Refresh();
    }

    public void Load(SavedFileRowViewModel row)
    {
        if (_storage == null || _loader == null) return;
        try
        {
            var ok = _loader(_storage.ReadText(row.File), row.File.DisplayName);
            StatusMessage = ok ? null : $"{row.DisplayName} couldn't be loaded (see the error above).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open {row.DisplayName}: {ex.Message}";
        }
    }

    public void ToggleArchive(SavedFileRowViewModel row) => Run(() =>
    {
        if (row.File.IsArchived) _storage!.Unarchive(row.File); else _storage!.Archive(row.File);
    });

    /// <summary>Two-step delete: the first call asks for confirmation on the row, the second deletes.</summary>
    public void RequestDelete(SavedFileRowViewModel row)
    {
        if (!row.ConfirmingDelete)
        {
            foreach (var other in Rows) other.ConfirmingDelete = false;
            row.ConfirmingDelete = true;
            return;
        }
        Run(() => _storage!.Delete(row.File));
    }

    public void CancelDelete(SavedFileRowViewModel row) => row.ConfirmingDelete = false;

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded) Refresh();
    }

    [RelayCommand]
    private void ToggleArchiveView() => ShowArchived = !ShowArchived;

    [RelayCommand]
    private void SelectAll()
    {
        var all = Rows.Count > 0 && Rows.All(r => r.IsSelected);
        foreach (var r in Rows) r.IsSelected = !all;
    }

    [RelayCommand]
    private void ArchiveSelected()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        Run(() =>
        {
            foreach (var r in selected)
            {
                if (ShowArchived) _storage!.Unarchive(r.File); else _storage!.Archive(r.File);
            }
        });
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (!ConfirmingBulkDelete)
        {
            ConfirmingBulkDelete = true;
            return;
        }
        var selected = Rows.Where(r => r.IsSelected).ToList();
        Run(() =>
        {
            foreach (var r in selected) _storage!.Delete(r.File);
        });
    }

    [RelayCommand]
    private void CancelBulkDelete() => ConfirmingBulkDelete = false;

    private void Run(Action action)
    {
        if (_storage == null) return;
        try
        {
            action();
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        Refresh();
    }
}
