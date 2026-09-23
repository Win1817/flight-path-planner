using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

/// <summary>Mirrors src/components/ReportPanel.tsx + src/pages/Index.tsx's report state:
/// a searchable AoR picker (the plain search-and-list variant, not a popover combobox —
/// keeps the same proven pattern used by the OPS/AoR tabs instead of introducing an
/// unverified AutoCompleteBox control) whose selection drives a live match count against
/// the OPS tab's *currently filtered* ops (respecting its search/closure/timeframe filters,
/// exactly like the web app's filteredOps dependency).</summary>
public partial class ReportTabViewModel : ViewModelBase
{
    private readonly OpsTabViewModel _opsTab;
    private readonly AorTabViewModel _aorTab;
    private List<ReportAorOptionViewModel> _allAorOptions = new();

    /// <summary>Which matched op's details are open, tracked by dataset ordinal (not the OpsRowViewModel instance
    /// itself) because MatchingOps is rebuilt from scratch on every refresh — mirrors OpsTabViewModel's ActiveOp.</summary>
    private int? _activeOrdinal;

    public ReportTabViewModel(OpsTabViewModel opsTab, AorTabViewModel aorTab, LocalStorageService? storage = null)
    {
        SavedFiles = new SavedFilesViewModel(storage, StorageCategory.Report);
        _opsTab = opsTab;
        _aorTab = aorTab;
        _opsTab.PropertyChanged += OnOpsTabPropertyChanged;
        _aorTab.PropertyChanged += OnAorTabPropertyChanged;
        RefreshAorOptions();
    }

    /// <summary>Every exported report is also kept in the local data folder.</summary>
    public SavedFilesViewModel SavedFiles { get; }

    [ObservableProperty]
    public partial ObservableCollection<ReportAorOptionViewModel> AorOptions { get; set; } = new();

    [ObservableProperty]
    public partial string AorSearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial ReportAorOptionViewModel? SelectedAor { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<OpsRowViewModel> MatchingOps { get; set; } = new();

    [ObservableProperty]
    public partial OpsRowViewModel? ActiveOp { get; set; }

    partial void OnActiveOpChanged(OpsRowViewModel? value) => _activeOrdinal = value?.Op.Ordinal;

    [RelayCommand]
    private void ActivateOp(OpsRowViewModel row) => ActiveOp = ReferenceEquals(ActiveOp, row) ? null : row;

    [RelayCommand]
    private void CloseDetails() => ActiveOp = null;

    public bool HasOpsAndAors => _opsTab.HasOps && _aorTab.HasAors;
    public int AorCount => _aorTab.TotalCount;
    public string MissingDataText =>
        !_opsTab.HasOps && !_aorTab.HasAors ? "Upload both OPS and AoR data to generate a report."
        : !_opsTab.HasOps ? "Upload OPS data to generate a report."
        : "Upload AoR data to generate a report.";
    public int MatchCount => MatchingOps.Count;
    public bool HasSelectedAor => SelectedAor != null;
    public bool HasNoMatches => HasSelectedAor && MatchCount == 0;

    private void OnOpsTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpsTabViewModel.FilteredOps) or nameof(OpsTabViewModel.HasOps))
        {
            OnPropertyChanged(nameof(HasOpsAndAors));
            OnPropertyChanged(nameof(MissingDataText));
            RefreshMatches();
        }
    }

    private void OnAorTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AorTabViewModel.AllAors))
        {
            OnPropertyChanged(nameof(HasOpsAndAors));
            OnPropertyChanged(nameof(MissingDataText));
            OnPropertyChanged(nameof(AorCount));
            RefreshAorOptions();
        }
    }

    private void RefreshAorOptions()
    {
        var previouslySelectedId = SelectedAor?.Id;
        _allAorOptions = _aorTab.AllAors.Select(a => new ReportAorOptionViewModel(a)).ToList();
        ApplyAorSearchFilter();
        SelectedAor = previouslySelectedId != null
            ? _allAorOptions.FirstOrDefault(o => o.Id == previouslySelectedId)
            : null;
    }

    partial void OnAorSearchQueryChanged(string value) => ApplyAorSearchFilter();

    private void ApplyAorSearchFilter()
    {
        IEnumerable<ReportAorOptionViewModel> result = _allAorOptions;
        if (!string.IsNullOrWhiteSpace(AorSearchQuery))
        {
            var query = AorSearchQuery.Trim().ToLowerInvariant();
            result = result.Where(o =>
                o.Name.ToLowerInvariant().Contains(query) ||
                o.Designator.ToLowerInvariant().Contains(query) ||
                o.Id.ToLowerInvariant().Contains(query));
        }
        AorOptions = new ObservableCollection<ReportAorOptionViewModel>(result);
    }

    partial void OnSelectedAorChanged(ReportAorOptionViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedAor));
        _activeOrdinal = null; // a new AoR means a new match set; don't carry details over from the old one
        RefreshMatches();
    }

    private void RefreshMatches()
    {
        if (SelectedAor == null)
        {
            MatchingOps = new ObservableCollection<OpsRowViewModel>();
        }
        else
        {
            var currentOps = _opsTab.FilteredOpData;
            var matched = ReportService.GetOpsInAor(currentOps, SelectedAor.Aor);
            MatchingOps = new ObservableCollection<OpsRowViewModel>(matched.Select(op => new OpsRowViewModel(op)));
        }
        // Re-resolve rather than carry the old instance: MatchingOps was just rebuilt from scratch, so any
        // previously-active row reference is now stale. Closes the panel if the op fell out of the match set.
        ActiveOp = _activeOrdinal is int ordinal ? MatchingOps.FirstOrDefault(r => r.Op.Ordinal == ordinal) : null;
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private sealed class ReportExportRow
    {
        public string GeozoneId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Restriction { get; init; } = "";
        public int PlansMatched { get; init; }
    }

    /// <summary>A report export whose heavy work (matching every AoR against every filtered operation) is done lazily, once, on
    /// whichever thread first asks — so JSON, XLSX and the local copy of one export share a single computation.</summary>
    public sealed class SummaryExport
    {
        private readonly Lazy<List<ReportExportRow>> _rows;

        internal SummaryExport(IReadOnlyList<ParsedAor> aors, IReadOnlyList<ParsedOps> ops) =>
            _rows = new Lazy<List<ReportExportRow>>(() =>
                ReportService.GetAorReportSummary(aors, ops).Select(s => new ReportExportRow
                {
                    GeozoneId = string.IsNullOrEmpty(s.Aor.Designator) ? s.Aor.Id : s.Aor.Designator,
                    Name = s.Aor.Name,
                    Restriction = s.Aor.Restriction ?? "",
                    PlansMatched = s.MatchCount,
                }).ToList());

        public string BuildJson() =>
            JsonSerializer.Serialize(new { comment = $"Total number of geozones: {_rows.Value.Count}", data = _rows.Value }, new JsonSerializerOptions { WriteIndented = true });

        public byte[] BuildXlsx()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Report");
            sheet.Cell(1, 1).InsertTable(_rows.Value);
            // AdjustToContents measures every cell's rendered text, which is fine for a handful of rows but turns
            // into a real cost (tens of seconds, large transient allocations) once exports reach tens of thousands
            // of rows - a fixed width is a reasonable trade-off at that scale.
            sheet.Columns().Width = 18;
            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }
    }

    /// <summary>Captures the immutable AoR and operation snapshots now (UI thread); the returned export can be built on a worker thread.</summary>
    public SummaryExport PrepareSummaryExport() => new(_aorTab.AllAors, _opsTab.FilteredOpData);

    /// <summary>Keeps a local copy of a finished export (always JSON, as the web app did).</summary>
    public async Task SaveLocalCopyAsync(SummaryExport export)
    {
        var json = await Task.Run(export.BuildJson);
        await SavedFiles.SaveFromSourceAsync(Services.Import.ImportSource.FromText(LocalCopyName, json));
    }

    private static string LocalCopyName => $"geozone-report-{DateTime.UtcNow:yyyy-MM-dd}.json";

    public string ExportSummaryToJson()
    {
        var export = PrepareSummaryExport();
        var json = export.BuildJson();
        SavedFiles.SaveNew(LocalCopyName, json);
        return json;
    }

    public byte[] ExportSummaryToXlsxBytes()
    {
        var export = PrepareSummaryExport();
        SavedFiles.SaveNew(LocalCopyName, export.BuildJson());
        return export.BuildXlsx();
    }
}
