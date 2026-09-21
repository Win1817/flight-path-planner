using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;

namespace FlightPathPlanner.ViewModels;

/// <summary>AoR/GeoZone tab. The imported dataset is immutable and lives off to the side (arrays + a spatial index); the UI sees it
/// through a filter mask, a selection set and a virtual row list, so nothing scales with the dataset except plain arrays.
/// Imports run on a background thread, report throttled progress, are cancellable, and replace the dataset in one step.</summary>
public partial class AorTabViewModel : ViewModelBase
{
    /// <summary>Up to this many AoRs, typing filters instantly; above it, filtering waits for a short pause in typing.</summary>
    public const int ImmediateFilterLimit = 2000;
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>Files at least this big trigger the "large file" notice before importing.</summary>
    public const long LargeFileBytes = 40L * 1024 * 1024;

    private AorDataset _dataset = AorDataset.Empty;
    private bool[]? _mask;                 // null = every AoR passes the filter
    private int[] _visible = Array.Empty<int>();
    private readonly HashSet<int> _selected = new(); // dataset positions (ParsedAor.Ordinal)
    private int? _activeOrdinal;
    private ParsedAor[]? _filteredData;
    private CancellationTokenSource? _filterCts;

    public AorTabViewModel(LocalStorageService? storage = null)
    {
        Import = new ImportViewModel();
        FilteredAors = new VirtualRowList<AorRowViewModel>(CreateRow);
        SavedFiles = new SavedFilesViewModel(storage, StorageCategory.Aor, source => ImportAsync(source, persist: false));
    }

    public ImportViewModel Import { get; }
    public SavedFilesViewModel SavedFiles { get; }

    /// <summary>The rows currently shown. Materialises a row only when the list control asks for it.</summary>
    public VirtualRowList<AorRowViewModel> FilteredAors { get; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial AorRowViewModel? ActiveAor { get; set; }

    [ObservableProperty]
    public partial string? UploadedFileName { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial ImportSource? PendingLargeSource { get; set; }

    // ---- Dataset / filter state ----
    public AorDataset Dataset => _dataset;
    public IReadOnlyList<ParsedAor> AllAors => _dataset.Items;
    public bool[]? FilterMask => _mask;
    public int TotalCount => _dataset.Items.Length;
    public int FilteredCount => _visible.Length;
    public bool HasAors => TotalCount > 0;
    public bool HasFilteredAors => FilteredCount > 0;
    public bool HasNoFilteredAors => HasAors && FilteredCount == 0;

    /// <summary>The AoRs that pass the current filter, in dataset order (built lazily, then cached until the filter changes).</summary>
    public IReadOnlyList<ParsedAor> FilteredAorData =>
        _filteredData ??= _visible.Select(i => _dataset.Items[i]).ToArray();

    // ---- Selection (only AoRs passing the filter count, as in the web app) ----
    private bool IsVisible(int position) => _mask == null || _mask[position];
    public int SelectedCount => _selected.Count(IsVisible);
    public bool HasSelection => SelectedCount > 0;
    public bool AllSelected => FilteredCount > 0 && SelectedCount == FilteredCount;

    /// <summary>Selected AoRs that are currently visible, in dataset order.</summary>
    public IReadOnlyList<ParsedAor> SelectedAors =>
        _selected.Where(IsVisible).OrderBy(i => i).Select(i => _dataset.Items[i]).ToArray();

    public double TotalSelectedArea => SelectedAors.Sum(a => a.ComputedArea);
    public string TotalSelectedAreaDisplay => OpsParser.FormatArea(TotalSelectedArea);
    public string SelectAllLabel => AllSelected && FilteredCount > 0
        ? $"Deselect all ({SelectedCount}/{FilteredCount})"
        : $"Select all ({SelectedCount}/{FilteredCount})";

    public bool HasPendingLargeSource => PendingLargeSource != null;
    public string PendingLargeText => PendingLargeSource == null ? "" :
        $"{PendingLargeSource.Name} is {ImportViewModel.FormatBytes(PendingLargeSource.Length)}. The app will load it in the background with " +
        "optimised loading and draw only the zones in the map's current view. You can keep working, and cancel at any time.";

    partial void OnPendingLargeSourceChanged(ImportSource? value)
    {
        OnPropertyChanged(nameof(HasPendingLargeSource));
        OnPropertyChanged(nameof(PendingLargeText));
    }

    // =====================================================================================================
    //  Import
    // =====================================================================================================

    /// <summary>Entry point for the UI: shows the large-file notice for very big files, otherwise imports straight away.</summary>
    public Task<bool> RequestImportAsync(ImportSource source)
    {
        if (source.Length >= LargeFileBytes)
        {
            PendingLargeSource = source;
            return Task.FromResult(false);
        }
        return ImportAsync(source, persist: true);
    }

    [RelayCommand]
    private async Task ConfirmLargeImportAsync()
    {
        if (PendingLargeSource is not { } source) return;
        PendingLargeSource = null;
        await ImportAsync(source, persist: true);
    }

    [RelayCommand]
    private void CancelLargeImport() => PendingLargeSource = null;

    /// <summary>Imports a file without blocking the UI: streaming parse on a worker thread, throttled progress, cancellable via
    /// <see cref="ImportViewModel"/>. The current dataset is untouched unless the import succeeds.</summary>
    public async Task<bool> ImportAsync(ImportSource source, bool persist)
    {
        if (!Import.TryBegin("Importing GeoZones", source)) return false;
        var token = Import.Token;
        var context = SynchronizationContext.Current;
        void OnUi(Action action)
        {
            if (context != null) context.Post(_ => action(), null); else action();
        }

        try
        {
            var dataset = await Task.Run(
                () => AorImporter.ImportAsync(source, p => OnUi(() => Import.Update(p)), token), token).ConfigureAwait(true);

            Commit(dataset, source.Name);
            if (persist) await SavedFiles.SaveFromSourceAsync(source, token);
            Import.Complete(dataset.Metrics);
            AppLog.Diagnostic(dataset.Metrics.ToDiagnosticText());
            ReportOutcome(dataset, source.Name);
            return true;
        }
        catch (OperationCanceledException)
        {
            Import.MarkCancelled();
            Notifier.Info($"Import of {source.Name} cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Import.MarkFailed(ex.Message);
            ErrorMessage = ex.Message;
            Notifier.Error($"Couldn't load {source.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Synchronous convenience for callers that already hold the whole text (tests, very small inputs). Real uploads use
    /// <see cref="ImportAsync"/>.</summary>
    /// <returns>true if the file parsed and was loaded.</returns>
    public bool LoadFromJson(string json, string fileName, bool persist = true)
    {
        try
        {
            var source = ImportSource.FromText(fileName, json);
            var dataset = AorImporter.ImportAsync(source, null, CancellationToken.None).GetAwaiter().GetResult();
            Commit(dataset, fileName);
            if (persist) SavedFiles.SaveNew(fileName, json);
            ReportOutcome(dataset, fileName);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Notifier.Error($"Couldn't load {fileName}: {ex.Message}");
            Replace(AorDataset.Empty);
            return false;
        }
    }

    private void ReportOutcome(AorDataset dataset, string fileName)
    {
        if (dataset.Skipped > 0)
            Notifier.Warning($"Loaded {dataset.Items.Length:N0} areas from {fileName}; {dataset.Skipped} entr{(dataset.Skipped == 1 ? "y was" : "ies were")} skipped");
        else
            Notifier.Success($"Loaded {dataset.Items.Length:N0} area{(dataset.Items.Length == 1 ? "" : "s")} from {fileName}");
    }

    /// <summary>Swaps in a freshly imported dataset: one UI-thread step that assigns arrays and resets the (virtual) list once.</summary>
    private void Commit(AorDataset dataset, string fileName)
    {
        UploadedFileName = fileName;
        ErrorMessage = dataset.Skipped > 0
            ? $"Loaded with {dataset.Skipped} entr{(dataset.Skipped == 1 ? "y" : "ies")} skipped (unrecognized format)."
            : null;
        _selected.Clear();
        _activeOrdinal = null;
        _filterCts?.Cancel();
        SearchQuery = ""; // partial handler is a no-op while the dataset swap below re-filters
        Replace(dataset);
    }

    private void Replace(AorDataset dataset)
    {
        _dataset = dataset;
        ActiveAor = null;
        _activeOrdinal = null;
        OnPropertyChanged(nameof(AllAors));
        OnPropertyChanged(nameof(Dataset));
        ApplyFilterNow();
    }

    // =====================================================================================================
    //  Filtering
    // =====================================================================================================

    partial void OnSearchQueryChanged(string value)
    {
        _filterCts?.Cancel();
        if (_dataset.Items.Length <= ImmediateFilterLimit)
        {
            ApplyFilterNow();
            return;
        }

        // Large dataset: wait for a pause in typing, then scan off the UI thread and apply the result in one step.
        var cts = _filterCts = new CancellationTokenSource();
        var context = SynchronizationContext.Current;
        var dataset = _dataset;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FilterDebounce, cts.Token).ConfigureAwait(false);
                var (mask, visible) = ComputeFilter(dataset, value, cts.Token);
                void Apply()
                {
                    if (cts.IsCancellationRequested || !ReferenceEquals(dataset, _dataset)) return;
                    ApplyFilter(mask, visible);
                }
                if (context != null) context.Post(_ => Apply(), null); else Apply();
            }
            catch (OperationCanceledException) { /* superseded by newer input */ }
        });
    }

    private void ApplyFilterNow()
    {
        var (mask, visible) = ComputeFilter(_dataset, SearchQuery, CancellationToken.None);
        ApplyFilter(mask, visible);
    }

    private static (bool[]? Mask, int[] Visible) ComputeFilter(AorDataset dataset, string query, CancellationToken ct)
    {
        var items = dataset.Items;
        if (string.IsNullOrWhiteSpace(query))
        {
            var all = new int[items.Length];
            for (int i = 0; i < all.Length; i++) all[i] = i;
            return (null, all);
        }

        var needle = query.Trim().ToLowerInvariant();
        var mask = new bool[items.Length];
        var visible = new List<int>();
        for (int i = 0; i < items.Length; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (items[i].SearchText.Contains(needle, StringComparison.Ordinal))
            {
                mask[i] = true;
                visible.Add(i);
            }
        }
        return (mask, visible.ToArray());
    }

    private void ApplyFilter(bool[]? mask, int[] visible)
    {
        _mask = mask;
        _visible = visible;
        _filteredData = null;
        FilteredAors.Reset(visible);
        ActiveAor = _activeOrdinal is int ordinal ? FilteredAors.RowAtPosition(ordinal) : null;

        OnPropertyChanged(nameof(FilteredAors));
        OnPropertyChanged(nameof(FilteredAorData));
        OnPropertyChanged(nameof(FilterMask));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasAors));
        OnPropertyChanged(nameof(HasFilteredAors));
        OnPropertyChanged(nameof(HasNoFilteredAors));
        RaiseSelectionChanged();
    }

    // =====================================================================================================
    //  Rows, selection, details
    // =====================================================================================================

    private AorRowViewModel CreateRow(int position) =>
        new(_dataset.Items[position], OnRowSelectionChanged, _selected.Contains(position));

    private void OnRowSelectionChanged(AorRowViewModel row, bool selected)
    {
        if (selected) _selected.Add(row.Aor.Ordinal); else _selected.Remove(row.Aor.Ordinal);
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(TotalSelectedArea));
        OnPropertyChanged(nameof(TotalSelectedAreaDisplay));
        OnPropertyChanged(nameof(SelectAllLabel));
    }

    partial void OnActiveAorChanged(AorRowViewModel? value) => _activeOrdinal = value?.Aor.Ordinal;

    [RelayCommand]
    private void ActivateAor(AorRowViewModel row) => ActiveAor = ReferenceEquals(ActiveAor, row) ? null : row;

    [RelayCommand]
    private void CloseDetails() => ActiveAor = null;

    /// <summary>Row for an AoR id if it is currently visible (used when a shape is clicked on the map).</summary>
    public AorRowViewModel? FindVisibleRow(string id)
    {
        foreach (var position in _visible)
        {
            if (_dataset.Items[position].Id == id) return FilteredAors.RowAtPosition(position);
        }
        return null;
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool select = !AllSelected;
        foreach (var position in _visible)
        {
            if (select) _selected.Add(position); else _selected.Remove(position);
        }
        foreach (var row in FilteredAors.MaterialisedRows) row.SetSelectedSilently(_selected.Contains(row.Aor.Ordinal));
        RaiseSelectionChanged();
    }

    // =====================================================================================================
    //  Removing AoRs from the session
    // =====================================================================================================

    [RelayCommand]
    private void DeleteAor(AorRowViewModel row)
    {
        Notifier.Info("Removed 1 area from the current session");
        RemoveOrdinals(new HashSet<int> { row.Aor.Ordinal });
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var doomed = _selected.Where(IsVisible).ToHashSet();
        if (doomed.Count == 0) return;
        Notifier.Info($"Removed {doomed.Count:N0} area{(doomed.Count == 1 ? "" : "s")} from the current session");
        RemoveOrdinals(doomed);
    }

    /// <summary>Rebuilds the (immutable) dataset without the given AoRs. Rare and O(n); ordinals are reassigned and the surviving
    /// selection is remapped so it stays attached to the same AoRs.</summary>
    private void RemoveOrdinals(HashSet<int> remove)
    {
        var old = _dataset.Items;
        var survivingSelection = old.Where(a => _selected.Contains(a.Ordinal) && !remove.Contains(a.Ordinal)).ToList();
        var kept = old.Where(a => !remove.Contains(a.Ordinal)).ToArray();
        for (int i = 0; i < kept.Length; i++) kept[i].Ordinal = i;

        var index = new SpatialFeatureIndex(kept.Select(a => a.Bounds).ToArray(), kept.Select(a => a.ComputedArea).ToArray());
        var dataset = new AorDataset(kept, index, _dataset.Skipped, _dataset.Metrics);

        _selected.Clear();
        foreach (var a in survivingSelection) _selected.Add(a.Ordinal);
        if (_activeOrdinal is int active && remove.Contains(active)) _activeOrdinal = null;
        else if (_activeOrdinal is int a2) _activeOrdinal = old[a2].Ordinal;

        _dataset = dataset;
        OnPropertyChanged(nameof(AllAors));
        OnPropertyChanged(nameof(Dataset));
        ApplyFilterNow();
    }

    // =====================================================================================================
    //  Export
    // =====================================================================================================

    private sealed class AorExportRow
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Designator { get; init; } = "";
        public double LowerLimit { get; init; }
        public double UpperLimit { get; init; }
        public string VerticalLimitsUom { get; init; } = "";
        public string VerticalReferenceType { get; init; } = "";
        public bool? AutoReject { get; init; }
        public bool? AutoApprovalEnabled { get; init; }
        public bool? AorEnabled { get; init; }
        public bool? AutoTakeOffClearanceEnabled { get; init; }
        public bool? MaxSimultaneousOperationsEnabled { get; init; }
        public double? MaxSimultaneousOperations { get; init; }
        public string? FeatureType { get; init; }
        public double AreaM2 { get; init; }
        public double AreaKm2 { get; init; }
    }

    private List<AorExportRow> BuildExportRows() =>
        SelectedAors.Select(a => new AorExportRow
        {
            Id = a.Id,
            Name = a.Name,
            Designator = a.Designator,
            LowerLimit = a.LowerLimit,
            UpperLimit = a.UpperLimit,
            VerticalLimitsUom = a.VerticalLimitsUom,
            VerticalReferenceType = a.VerticalReferenceType,
            AutoReject = a.AutoReject,
            AutoApprovalEnabled = a.AutoApprovalEnabled,
            AorEnabled = a.AorEnabled,
            AutoTakeOffClearanceEnabled = a.AutoTakeOffClearanceEnabled,
            MaxSimultaneousOperationsEnabled = a.MaxSimultaneousOperationsEnabled,
            MaxSimultaneousOperations = a.MaxSimultaneousOperations,
            FeatureType = a.FeatureType,
            AreaM2 = a.ComputedArea,
            AreaKm2 = a.ComputedArea / 1_000_000,
        }).ToList();

    /// <summary>Captures the selection now (UI thread, cheap) and returns a builder that can run on a worker thread.</summary>
    public Func<string> PrepareSelectedJsonExport() { var rows = BuildExportRows(); return () => SerializeJson(rows); }
    public Func<byte[]> PrepareSelectedXlsxExport() { var rows = BuildExportRows(); return () => SerializeXlsx(rows); }

    public string ExportSelectedToJson() => SerializeJson(BuildExportRows());
    public byte[] ExportSelectedToXlsxBytes() => SerializeXlsx(BuildExportRows());

    private static string SerializeJson(List<AorExportRow> rows) =>
        JsonSerializer.Serialize(new { comment = $"Total number of AoRs: {rows.Count}", data = rows }, new JsonSerializerOptions { WriteIndented = true });

    private static byte[] SerializeXlsx(List<AorExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("AoRs");
        sheet.Cell(1, 1).InsertTable(rows);
        sheet.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
