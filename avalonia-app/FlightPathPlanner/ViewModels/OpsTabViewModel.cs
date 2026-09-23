using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;

namespace FlightPathPlanner.ViewModels;

/// <summary>OPS tab, built like the AoR tab: an immutable imported dataset (arrays + spatial index), a filter mask, a selection set
/// and a virtual row list, with a streaming, cancellable, off-UI-thread import.</summary>
public partial class OpsTabViewModel : ViewModelBase
{
    /// <summary>Up to this many operations, filters apply instantly on the UI thread; above it, off-thread with a typing pause.</summary>
    public const int ImmediateFilterLimit = 2000;
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(250);
    public const long LargeFileBytes = 40L * 1024 * 1024;

    private OpsDataset _dataset = OpsDataset.Empty;
    private bool[]? _mask;
    private int[] _visible = Array.Empty<int>();
    private readonly HashSet<int> _selected = new();
    private int? _activeOrdinal;
    private ParsedOps[]? _filteredData;
    private CancellationTokenSource? _filterCts;
    private bool _suppressFilter;

    public OpsTabViewModel(LocalStorageService? storage = null)
    {
        Import = new ImportViewModel();
        FilteredOps = new VirtualRowList<OpsRowViewModel>(CreateRow);
        SavedFiles = new SavedFilesViewModel(storage, StorageCategory.Ops, source => ImportAsync(source, persist: false));
    }

    public ImportViewModel Import { get; }

    /// <summary>Uploads are kept in the local data folder; this panel reloads/archives/deletes them.</summary>
    public SavedFilesViewModel SavedFiles { get; }

    /// <summary>The rows currently shown. Materialises a row only when the list control asks for it.</summary>
    public VirtualRowList<OpsRowViewModel> FilteredOps { get; }

    [ObservableProperty]
    public partial ObservableCollection<ClosureReasonChipViewModel> ClosureReasonChips { get; set; } = new();

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial DateTimeOffset? TimeframeFrom { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? TimeframeTo { get; set; }

    [ObservableProperty]
    public partial OpsRowViewModel? ActiveOp { get; set; }

    [ObservableProperty]
    public partial string? UploadedFileName { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial ImportSource? PendingLargeSource { get; set; }

    /// <summary>Whether the closure-reason and timeframe filters are expanded (collapsing them leaves more room for the list).</summary>
    [ObservableProperty]
    public partial bool FiltersVisible { get; set; } = true;

    // ---- Dataset / filter state ----
    public OpsDataset Dataset => _dataset;
    public bool[]? FilterMask => _mask;
    public int TotalCount => _dataset.Items.Length;
    public int FilteredCount => _visible.Length;
    public bool HasOps => TotalCount > 0;
    public bool HasFilteredOps => FilteredCount > 0;
    public bool HasNoFilteredOps => HasOps && FilteredCount == 0;
    public bool HasClosureReasons => ClosureReasonChips.Count > 0;

    /// <summary>The operations passing the current filters, in dataset order (built lazily, cached until the filters change).</summary>
    public IReadOnlyList<ParsedOps> FilteredOpData => _filteredData ??= _visible.Select(i => _dataset.Items[i]).ToArray();

    // ---- Selection (only visible operations count) ----
    private bool IsVisible(int position) => _mask == null || _mask[position];
    public int SelectedCount => _selected.Count(IsVisible);
    public bool HasSelection => SelectedCount > 0;
    public bool AllSelected => FilteredCount > 0 && SelectedCount == FilteredCount;
    public IReadOnlyList<ParsedOps> SelectedOps => _selected.Where(IsVisible).OrderBy(i => i).Select(i => _dataset.Items[i]).ToArray();
    public double TotalSelectedArea => SelectedOps.Sum(o => o.ComputedArea);
    public string TotalSelectedAreaDisplay => OpsParser.FormatArea(TotalSelectedArea);
    public string SelectAllLabel => AllSelected && FilteredCount > 0
        ? $"Deselect all ({SelectedCount}/{FilteredCount})"
        : $"Select all ({SelectedCount}/{FilteredCount})";

    public bool HasPendingLargeSource => PendingLargeSource != null;
    public string PendingLargeText => PendingLargeSource == null ? "" :
        $"{PendingLargeSource.Name} is {ImportViewModel.FormatBytes(PendingLargeSource.Length)}. The app will load it in the background with " +
        "optimised loading and draw only the operations in the map's current view. You can keep working, and cancel at any time.";

    partial void OnPendingLargeSourceChanged(ImportSource? value)
    {
        OnPropertyChanged(nameof(HasPendingLargeSource));
        OnPropertyChanged(nameof(PendingLargeText));
    }

    // =====================================================================================================
    //  Import
    // =====================================================================================================

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

    /// <summary>Streaming import on a worker thread with throttled progress; the current dataset is untouched unless it succeeds.</summary>
    public async Task<bool> ImportAsync(ImportSource source, bool persist)
    {
        if (!Import.TryBegin("Importing operations", source)) return false;
        var token = Import.Token;
        var context = SynchronizationContext.Current;
        void OnUi(Action action)
        {
            if (context != null) context.Post(_ => action(), null); else action();
        }

        try
        {
            var dataset = await Task.Run(
                () => OpsImporter.ImportAsync(source, p => OnUi(() => Import.Update(p)), token), token).ConfigureAwait(true);

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

    /// <summary>Synchronous convenience for callers that already hold the whole text (tests, tiny inputs). Uploads use <see cref="ImportAsync"/>.</summary>
    /// <returns>true if the file parsed and was loaded.</returns>
    public bool LoadFromJson(string json, string fileName, bool persist = true)
    {
        try
        {
            var dataset = OpsImporter.ImportAsync(ImportSource.FromText(fileName, json), null, CancellationToken.None).GetAwaiter().GetResult();
            Commit(dataset, fileName);
            if (persist) SavedFiles.SaveNew(fileName, json);
            ReportOutcome(dataset, fileName);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Notifier.Error($"Couldn't load {fileName}: {ex.Message}");
            Replace(OpsDataset.Empty);
            return false;
        }
    }

    private void ReportOutcome(OpsDataset dataset, string fileName)
    {
        if (dataset.Skipped > 0)
            Notifier.Warning($"Loaded {dataset.Items.Length:N0} operations from {fileName}; {dataset.Skipped} entr{(dataset.Skipped == 1 ? "y was" : "ies were")} skipped");
        else
            Notifier.Success($"Loaded {dataset.Items.Length:N0} operation{(dataset.Items.Length == 1 ? "" : "s")} from {fileName}");
    }

    private void Commit(OpsDataset dataset, string fileName)
    {
        _suppressFilter = true;
        try
        {
            UploadedFileName = fileName;
            ErrorMessage = dataset.Skipped > 0
                ? $"Loaded with {dataset.Skipped} entr{(dataset.Skipped == 1 ? "y" : "ies")} skipped (unrecognized format)."
                : null;
            _selected.Clear();
            _filterCts?.Cancel();
            SearchQuery = "";
            TimeframeFrom = dataset.OverallRange?.from;
            TimeframeTo = dataset.OverallRange?.to;
            RebuildClosureReasonChips(dataset);
        }
        finally { _suppressFilter = false; }
        Replace(dataset);
    }

    private void Replace(OpsDataset dataset)
    {
        _dataset = dataset;
        ActiveOp = null;
        _activeOrdinal = null;
        OnPropertyChanged(nameof(Dataset));
        ApplyFilterNow();
    }

    private void RebuildClosureReasonChips(OpsDataset dataset)
    {
        var previouslySelected = ClosureReasonChips.Where(c => c.IsSelected).Select(c => c.Reason).ToHashSet();
        var chips = new ObservableCollection<ClosureReasonChipViewModel>();
        foreach (var reason in dataset.ClosureReasons)
        {
            var chip = new ClosureReasonChipViewModel(reason) { IsSelected = previouslySelected.Contains(reason) };
            chip.PropertyChanged += OnClosureReasonChipChanged;
            chips.Add(chip);
        }
        ClosureReasonChips = chips;
        OnPropertyChanged(nameof(HasClosureReasons));
    }

    private void OnClosureReasonChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClosureReasonChipViewModel.IsSelected)) ScheduleFilter(debounce: false);
    }

    // =====================================================================================================
    //  Filtering
    // =====================================================================================================

    partial void OnSearchQueryChanged(string value) => ScheduleFilter(debounce: true);
    partial void OnTimeframeFromChanged(DateTimeOffset? value) => ScheduleFilter(debounce: false);
    partial void OnTimeframeToChanged(DateTimeOffset? value) => ScheduleFilter(debounce: false);

    private sealed record FilterInputs(string Query, DateTimeOffset? From, DateTimeOffset? To, HashSet<string> Reasons);

    private FilterInputs CaptureInputs() => new(
        SearchQuery, TimeframeFrom, TimeframeTo,
        ClosureReasonChips.Where(c => c.IsSelected).Select(c => c.Reason).ToHashSet());

    private void ScheduleFilter(bool debounce)
    {
        if (_suppressFilter) return;
        _filterCts?.Cancel();
        if (_dataset.Items.Length <= ImmediateFilterLimit)
        {
            ApplyFilterNow();
            return;
        }

        var cts = _filterCts = new CancellationTokenSource();
        var context = SynchronizationContext.Current;
        var dataset = _dataset;
        var inputs = CaptureInputs();
        _ = Task.Run(async () =>
        {
            try
            {
                if (debounce) await Task.Delay(FilterDebounce, cts.Token).ConfigureAwait(false);
                var (mask, visible) = ComputeFilter(dataset, inputs, cts.Token);
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
        var (mask, visible) = ComputeFilter(_dataset, CaptureInputs(), CancellationToken.None);
        ApplyFilter(mask, visible);
    }

    private static (bool[]? Mask, int[] Visible) ComputeFilter(OpsDataset dataset, FilterInputs f, CancellationToken ct)
    {
        var items = dataset.Items;
        bool hasTime = f.From.HasValue;
        bool hasReasons = f.Reasons.Count > 0;
        bool hasQuery = !string.IsNullOrWhiteSpace(f.Query);

        if (!hasTime && !hasReasons && !hasQuery)
        {
            var all = new int[items.Length];
            for (int i = 0; i < all.Length; i++) all[i] = i;
            return (null, all);
        }

        DateTimeOffset startOfDay = default, endOfDay = default;
        if (hasTime)
        {
            var from = f.From!.Value;
            startOfDay = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, TimeSpan.Zero);
            var toDate = f.To ?? from;
            endOfDay = new DateTimeOffset(toDate.Year, toDate.Month, toDate.Day, 23, 59, 59, TimeSpan.Zero).AddMilliseconds(999);
        }
        var needle = hasQuery ? f.Query.Trim().ToLowerInvariant() : "";

        var mask = new bool[items.Length];
        var visible = new List<int>();
        for (int i = 0; i < items.Length; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            var op = items[i];
            if (hasTime && !(op.StartTime <= endOfDay && op.EndTime >= startOfDay)) continue;
            if (hasReasons && (op.ClosureReason == null || !f.Reasons.Contains(op.ClosureReason))) continue;
            if (hasQuery && !op.SearchText.Contains(needle, StringComparison.Ordinal)) continue;
            mask[i] = true;
            visible.Add(i);
        }
        return (mask, visible.ToArray());
    }

    private void ApplyFilter(bool[]? mask, int[] visible)
    {
        _mask = mask;
        _visible = visible;
        _filteredData = null;
        FilteredOps.Reset(visible);
        ActiveOp = _activeOrdinal is int ordinal ? FilteredOps.RowAtPosition(ordinal) : null;

        OnPropertyChanged(nameof(FilteredOps));
        OnPropertyChanged(nameof(FilteredOpData));
        OnPropertyChanged(nameof(FilterMask));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasOps));
        OnPropertyChanged(nameof(HasFilteredOps));
        OnPropertyChanged(nameof(HasNoFilteredOps));
        RaiseSelectionChanged();
    }

    // =====================================================================================================
    //  Rows, selection, details
    // =====================================================================================================

    private OpsRowViewModel CreateRow(int position) =>
        new(_dataset.Items[position], OnRowSelectionChanged, _selected.Contains(position));

    private void OnRowSelectionChanged(OpsRowViewModel row, bool selected)
    {
        if (selected) _selected.Add(row.Op.Ordinal); else _selected.Remove(row.Op.Ordinal);
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

    partial void OnActiveOpChanged(OpsRowViewModel? value) => _activeOrdinal = value?.Op.Ordinal;

    [RelayCommand]
    private void ActivateOp(OpsRowViewModel row) => ActiveOp = ReferenceEquals(ActiveOp, row) ? null : row;

    [RelayCommand]
    private void CloseDetails() => ActiveOp = null;

    /// <summary>Row for an operation id if it is currently visible (used when a shape is clicked on the map).</summary>
    public OpsRowViewModel? FindVisibleRow(string operationPlanId)
    {
        foreach (var position in _visible)
        {
            if (_dataset.Items[position].OperationPlanId == operationPlanId) return FilteredOps.RowAtPosition(position);
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
        foreach (var row in FilteredOps.MaterialisedRows) row.SetSelectedSilently(_selected.Contains(row.Op.Ordinal));
        RaiseSelectionChanged();
    }

    [RelayCommand]
    private void ClearClosureReasons()
    {
        _suppressFilter = true;
        foreach (var chip in ClosureReasonChips) chip.IsSelected = false;
        _suppressFilter = false;
        ScheduleFilter(debounce: false);
    }

    // =====================================================================================================
    //  Removing operations from the session
    // =====================================================================================================

    [RelayCommand]
    private void DeleteOp(OpsRowViewModel row)
    {
        Notifier.Info("Removed 1 operation from the current session");
        RemoveOrdinals(new HashSet<int> { row.Op.Ordinal });
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var doomed = _selected.Where(IsVisible).ToHashSet();
        if (doomed.Count == 0) return;
        Notifier.Info($"Removed {doomed.Count:N0} operation{(doomed.Count == 1 ? "" : "s")} from the current session");
        RemoveOrdinals(doomed);
    }

    private void RemoveOrdinals(HashSet<int> remove)
    {
        var old = _dataset.Items;
        var survivingSelection = old.Where(o => _selected.Contains(o.Ordinal) && !remove.Contains(o.Ordinal)).ToList();
        var kept = old.Where(o => !remove.Contains(o.Ordinal)).ToArray();
        for (int i = 0; i < kept.Length; i++) kept[i].Ordinal = i;

        var index = new SpatialFeatureIndex(kept.Select(o => o.Bounds).ToArray(), kept.Select(o => o.ComputedArea).ToArray());
        var reasons = kept.Select(o => o.ClosureReason).Where(r => !string.IsNullOrEmpty(r)).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray()!;
        var dataset = new OpsDataset(kept, index, reasons!, _dataset.OverallRange, _dataset.Skipped, _dataset.Metrics);

        _selected.Clear();
        foreach (var o in survivingSelection) _selected.Add(o.Ordinal);
        if (_activeOrdinal is int active && remove.Contains(active)) _activeOrdinal = null;
        else if (_activeOrdinal is int a2) _activeOrdinal = old[a2].Ordinal;

        _suppressFilter = true;
        RebuildClosureReasonChips(dataset);
        _suppressFilter = false;
        _dataset = dataset;
        OnPropertyChanged(nameof(Dataset));
        ApplyFilterNow();
    }

    // =====================================================================================================
    //  Export
    // =====================================================================================================

    private sealed class OpsExportRow
    {
        public string OperationPlanId { get; init; } = "";
        public string? Operator { get; init; }
        public string? Title { get; init; }
        public string? State { get; init; }
        public string? ClosureReason { get; init; }
        public string? SubmitTime { get; init; }
        public string? UpdateTime { get; init; }
        public string TimeBegin { get; init; } = "";
        public string TimeEnd { get; init; } = "";
        public string? ActualTimeEnd { get; init; }
        public double? MinAltitudeValue { get; init; }
        public string? MinAltitudeType { get; init; }
        public string? MinAltitudeUnit { get; init; }
        public double? MaxAltitudeValue { get; init; }
        public string? MaxAltitudeType { get; init; }
        public string? MaxAltitudeUnit { get; init; }
        public double AreaM2 { get; init; }
        public double AreaKm2 { get; init; }
        public string? Color { get; init; }
    }

    private List<OpsExportRow> BuildExportRows() =>
        SelectedOps.SelectMany(o => o.OperationVolumes.Select(v => new OpsExportRow
        {
            OperationPlanId = o.OperationPlanId,
            Operator = o.Operator,
            Title = o.Title,
            State = o.State,
            ClosureReason = o.ClosureReason,
            SubmitTime = o.SubmitTime,
            UpdateTime = o.UpdateTime,
            TimeBegin = v.EffectiveTimeBegin,
            TimeEnd = v.EffectiveTimeEnd,
            ActualTimeEnd = v.ActualTimeEnd,
            MinAltitudeValue = v.MinAltitude?.AltitudeValue,
            MinAltitudeType = v.MinAltitude?.VerticalReference,
            MinAltitudeUnit = v.MinAltitude?.UnitsOfMeasure,
            MaxAltitudeValue = v.MaxAltitude?.AltitudeValue,
            MaxAltitudeType = v.MaxAltitude?.VerticalReference,
            MaxAltitudeUnit = v.MaxAltitude?.UnitsOfMeasure,
            AreaM2 = o.ComputedArea,
            AreaKm2 = o.ComputedArea / 1_000_000,
            Color = o.Color,
        })).ToList();

    private static string SerializeJson(List<OpsExportRow> rows) =>
        JsonSerializer.Serialize(new { comment = $"Total number of ops: {rows.Count}", data = rows }, new JsonSerializerOptions { WriteIndented = true });

    private static byte[] SerializeXlsx(List<OpsExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("OPS");
        sheet.Cell(1, 1).InsertTable(rows);
        // AdjustToContents measures every cell's rendered text, which is fine for a handful of rows but turns
        // into a real cost (tens of seconds, large transient allocations) once exports reach tens of thousands
        // of rows - a fixed width is a reasonable trade-off at that scale.
        sheet.Columns().Width = 18;
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Captures the selection now (UI thread, cheap) and returns a builder that can run on a worker thread.</summary>
    public Func<string> PrepareSelectedJsonExport() { var rows = BuildExportRows(); return () => SerializeJson(rows); }
    public Func<byte[]> PrepareSelectedXlsxExport() { var rows = BuildExportRows(); return () => SerializeXlsx(rows); }

    public string ExportSelectedToJson() => SerializeJson(BuildExportRows());
    public byte[] ExportSelectedToXlsxBytes() => SerializeXlsx(BuildExportRows());
}
