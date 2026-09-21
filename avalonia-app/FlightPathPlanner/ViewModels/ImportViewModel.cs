using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Services.Import;

namespace FlightPathPlanner.ViewModels;

/// <summary>The visible state of one import: progress while it runs, a summary when it ends. Shared by the OPS and AoR tabs.</summary>
public partial class ImportViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial ImportState State { get; set; } = ImportState.Idle;

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string StageText { get; set; } = "";

    [ObservableProperty]
    public partial string CountText { get; set; } = "";

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; } = true;

    [ObservableProperty]
    public partial string TimeText { get; set; } = "";

    [ObservableProperty]
    public partial string SummaryTitle { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> SummaryLines { get; set; } = Array.Empty<string>();

    [ObservableProperty]
    public partial bool SummaryHasWarnings { get; set; }

    public bool IsActive => State == ImportState.Importing;
    public bool HasSummary => State is ImportState.Completed or ImportState.Cancelled or ImportState.Failed;
    public bool IsFailed => State == ImportState.Failed;
    public bool IsCancelled => State == ImportState.Cancelled;
    public bool IsCompletedClean => State == ImportState.Completed && !SummaryHasWarnings;
    public bool IsCompletedWithWarnings => State == ImportState.Completed && SummaryHasWarnings;
    public bool CanStart => !IsActive;
    public string PercentText => IsIndeterminate ? "" : Percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    partial void OnStateChanged(ImportState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsCompletedClean));
        OnPropertyChanged(nameof(IsCompletedWithWarnings));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnSummaryHasWarningsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCompletedClean));
        OnPropertyChanged(nameof(IsCompletedWithWarnings));
    }

    partial void OnPercentChanged(double value) => OnPropertyChanged(nameof(PercentText));
    partial void OnIsIndeterminateChanged(bool value) => OnPropertyChanged(nameof(PercentText));

    /// <summary>Starts a new import; returns false if one is already running.</summary>
    public bool TryBegin(string title, ImportSource source)
    {
        if (IsActive) return false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        Title = title;
        StageText = "Reading file…";
        CountText = "";
        TimeText = "";
        Percent = 0;
        IsIndeterminate = source.Length <= 0;
        SummaryLines = Array.Empty<string>();
        SummaryHasWarnings = false;
        State = ImportState.Importing;
        return true;
    }

    public void Update(ImportProgress p)
    {
        if (!IsActive) return;
        StageText = p.Stage switch
        {
            ImportStage.Reading => "Reading file…",
            ImportStage.Parsing => "Parsing and processing geometry…",
            ImportStage.Indexing => "Building spatial index…",
            ImportStage.PreparingMap => "Preparing map…",
            _ => "Finishing…",
        };
        CountText = p.RecordsSkipped > 0
            ? $"{p.RecordsProcessed:N0} processed · {p.RecordsSkipped:N0} skipped"
            : $"{p.RecordsProcessed:N0} processed";
        if (p.Fraction is { } f)
        {
            IsIndeterminate = false;
            Percent = Math.Round(f * 100, 1);
        }
        var elapsed = FormatSpan(p.Elapsed);
        TimeText = p.EstimatedRemaining is { } eta ? $"{elapsed} elapsed · about {FormatSpan(eta)} left" : $"{elapsed} elapsed";
    }

    public void Complete(ImportMetrics m)
    {
        var lines = new List<string>
        {
            $"File size: {FormatBytes(m.FileSizeBytes)}",
            $"Records: {m.Records:N0}  ·  valid {m.Valid:N0}  ·  skipped {m.Skipped:N0}",
            $"Geometry: {m.Polygons:N0} polygons, {m.Vertices:N0} vertices",
            $"Processing time: {m.Total.TotalSeconds:0.0} s",
        };
        SummaryTitle = m.Skipped > 0 ? "Import complete with warnings" : "Import complete";
        SummaryHasWarnings = m.Skipped > 0;
        if (m.Skipped > 0) lines.Add($"{m.Skipped:N0} record{(m.Skipped == 1 ? "" : "s")} could not be parsed and were skipped.");
        SummaryLines = lines;
        State = ImportState.Completed;
    }

    public void MarkCancelled()
    {
        SummaryTitle = "Import cancelled";
        SummaryLines = new[] { "Nothing was changed; your previous data is still loaded." };
        SummaryHasWarnings = false;
        State = ImportState.Cancelled;
    }

    public void MarkFailed(string message)
    {
        SummaryTitle = "Import failed";
        SummaryLines = new[] { message, "Nothing was changed; your previous data is still loaded." };
        SummaryHasWarnings = true;
        State = ImportState.Failed;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void DismissSummary()
    {
        if (!IsActive) State = ImportState.Idle;
    }

    public static string FormatSpan(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{t.TotalSeconds:0.#} s" : $"{(int)t.TotalMinutes} min {t.Seconds} s";

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
