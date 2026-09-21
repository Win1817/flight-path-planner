using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlightPathPlanner.ViewModels;

/// <summary>The "Saved Locally" page: one document list per category, backed by each tab's own SavedFiles model.</summary>
public partial class SavedTabViewModel : ViewModelBase
{
    public SavedTabViewModel(SavedFilesViewModel ops, SavedFilesViewModel aor, SavedFilesViewModel report)
    {
        Ops = ops;
        Aor = aor;
        Report = report;
        foreach (var files in new[] { ops, aor, report })
        {
            files.PropertyChanged += OnFilesChanged;
        }
    }

    public SavedFilesViewModel Ops { get; }
    public SavedFilesViewModel Aor { get; }
    public SavedFilesViewModel Report { get; }

    public bool IsAvailable => Ops.IsAvailable;
    public bool IsUnavailable => !IsAvailable;
    public string LocationText => Ops.StorageLocation ?? "";
    public int TotalCount => Ops.ActiveCount + Aor.ActiveCount + Report.ActiveCount;

    // SavedFilesViewModel refreshes its counts by raising HeaderText.
    private void OnFilesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SavedFilesViewModel.HeaderText)) OnPropertyChanged(nameof(TotalCount));
    }
}
