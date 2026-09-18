using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public ReportTabViewModel(OpsTabViewModel opsTab, AorTabViewModel aorTab)
    {
        _opsTab = opsTab;
        _aorTab = aorTab;
        _opsTab.PropertyChanged += OnOpsTabPropertyChanged;
        _aorTab.PropertyChanged += OnAorTabPropertyChanged;
        RefreshAorOptions();
    }

    [ObservableProperty]
    public partial ObservableCollection<ReportAorOptionViewModel> AorOptions { get; set; } = new();

    [ObservableProperty]
    public partial string AorSearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial ReportAorOptionViewModel? SelectedAor { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<OpsRowViewModel> MatchingOps { get; set; } = new();

    public bool HasOpsAndAors => _opsTab.HasOps && _aorTab.HasAors;
    public int AorCount => _aorTab.TotalCount;
    public int MatchCount => MatchingOps.Count;
    public bool HasSelectedAor => SelectedAor != null;
    public bool HasNoMatches => HasSelectedAor && MatchCount == 0;

    private void OnOpsTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpsTabViewModel.FilteredOps) or nameof(OpsTabViewModel.HasOps))
        {
            OnPropertyChanged(nameof(HasOpsAndAors));
            RefreshMatches();
        }
    }

    private void OnAorTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AorTabViewModel.AllAors))
        {
            OnPropertyChanged(nameof(HasOpsAndAors));
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
            var currentOps = _opsTab.FilteredOps.Select(r => r.Op).ToList();
            var matched = ReportService.GetOpsInAor(currentOps, SelectedAor.Aor);
            MatchingOps = new ObservableCollection<OpsRowViewModel>(matched.Select(op => new OpsRowViewModel(op)));
        }
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

    private List<ReportExportRow> BuildExportRows()
    {
        var currentOps = _opsTab.FilteredOps.Select(r => r.Op).ToList();
        var summary = ReportService.GetAorReportSummary(_aorTab.AllAors, currentOps);
        return summary.Select(s => new ReportExportRow
        {
            GeozoneId = string.IsNullOrEmpty(s.Aor.Designator) ? s.Aor.Id : s.Aor.Designator,
            Name = s.Aor.Name,
            Restriction = s.Aor.Restriction ?? "",
            PlansMatched = s.MatchCount,
        }).ToList();
    }

    public string ExportSummaryToJson()
    {
        var rows = BuildExportRows();
        var exportObject = new { comment = $"Total number of geozones: {rows.Count}", data = rows };
        return JsonSerializer.Serialize(exportObject, new JsonSerializerOptions { WriteIndented = true });
    }

    public byte[] ExportSummaryToXlsxBytes()
    {
        var rows = BuildExportRows();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        sheet.Cell(1, 1).InsertTable(rows);
        sheet.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
