using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;

namespace FlightPathPlanner.ViewModels;

/// <summary>The "Saved locally" panel for one category: lists files kept in the portable data folder, and lets the user
/// reload, archive/restore and delete them (singly or in bulk). Mirrors the web app's SavedFilesPanel.</summary>
public partial class SavedFilesViewModel : ViewModelBase
{
    private readonly LocalStorageService? _storage;
    private readonly StorageCategory _category;
    private readonly Func<ImportSource, Task<bool>>? _loader;

    /// <param name="loader">Called with a streaming source for the saved file when the user reloads it; returns whether it
    /// loaded. Null for categories that can't be reloaded (reports).</param>
    /// <param name="title">Section title, e.g. "OPS uploads".</param>
    public SavedFilesViewModel(LocalStorageService? storage, StorageCategory category, Func<ImportSource, Task<bool>>? loader = null,
        string? title = null)
    {
        _storage = storage;
        _category = category;
        _loader = loader;
        Title = title ?? category switch
        {
            StorageCategory.Ops => "OPS uploads",
            StorageCategory.Aor => "AoR uploads",
            _ => "Report exports",
        };
        IsExpanded = true;
        Refresh();
    }

    public string Title { get; }

    /// <summary>Raised after a saved file was successfully reloaded into its tab.</summary>
    public event Action? Loaded;

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
    public string? StorageLocation => _storage?.RootDirectory;
    public bool CanLoad => _loader != null;
    public bool HasRows => Rows.Count > 0;
    public bool HasNoRows => !HasRows;
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string HeaderText => $"{Title} ({ActiveCount})";
    public string ConfirmBulkDeleteText => $"Permanently delete {SelectedCount} selected file(s)? This cannot be undone.";
    public string ArchiveToggleText => ShowArchived ? $"Back to saved ({ActiveCount})" : $"Archive ({ArchivedCount})";
    public string ArchiveSelectedText => ShowArchived ? "Restore selected" : "Archive selected";
    public string EmptyText => ShowArchived ? "Nothing archived." : "Nothing saved yet — uploads are saved automatically.";
    public string DeleteSelectedText => ConfirmingBulkDelete ? $"Confirm delete ({SelectedCount})" : "Delete selected";

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
        OnPropertyChanged(nameof(ConfirmBulkDeleteText));
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

    /// <summary>Saves a copy of an uploaded file by streaming it to disk (never through memory). Failures are shown in the panel.</summary>
    public async Task SaveFromSourceAsync(ImportSource source, CancellationToken ct = default)
    {
        if (_storage == null) return;
        try
        {
            await _storage.SaveStreamAsync(_category, source.Name, source.Open, ct);
            StatusMessage = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save a local copy: {ex.Message}";
        }
        Refresh();
    }

    public async Task LoadAsync(SavedFileRowViewModel row)
    {
        if (_storage == null || _loader == null) return;
        try
        {
            var file = row.File;
            var source = new ImportSource(file.DisplayName, file.SizeBytes, () => _storage.OpenRead(file));
            var ok = await _loader(source);
            StatusMessage = ok ? null : $"{row.DisplayName} couldn't be loaded (see the error above).";
            if (ok) Loaded?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open {row.DisplayName}: {ex.Message}";
        }
    }

    public void ToggleArchive(SavedFileRowViewModel row) => Run(() =>
    {
        if (row.File.IsArchived)
        {
            _storage!.Unarchive(row.File);
            Notifier.Success($"Restored {row.DisplayName}");
        }
        else
        {
            _storage!.Archive(row.File);
            Notifier.Info($"Archived {row.DisplayName}");
        }
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
        Run(() =>
        {
            _storage!.Delete(row.File);
            Notifier.Info($"Deleted {row.DisplayName}");
        });
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
            Notifier.Info(ShowArchived ? $"Restored {selected.Count} file(s)" : $"Archived {selected.Count} file(s)");
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
            Notifier.Info($"Deleted {selected.Count} file(s)");
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
            Notifier.Error(ex.Message);
        }
        Refresh();
    }
}
