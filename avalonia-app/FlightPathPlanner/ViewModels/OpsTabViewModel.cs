using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

/// <summary>Mirrors src/pages/Index.tsx's OPS state/filtering (filteredOps useMemo chain) plus
/// OpsList.tsx/OpsDetails.tsx, adapted to MVVM: filtering runs eagerly on any input change
/// instead of as a memoized selector.</summary>
public partial class OpsTabViewModel : ViewModelBase
{
    private List<ParsedOps> _allOps = new();

    [ObservableProperty]
    public partial ObservableCollection<OpsRowViewModel> FilteredOps { get; set; } = new();

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

    public int TotalCount => _allOps.Count;
    public int FilteredCount => FilteredOps.Count;
    public int SelectedCount => FilteredOps.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public bool HasOps => TotalCount > 0;
    public bool HasFilteredOps => FilteredCount > 0;
    public bool HasNoFilteredOps => HasOps && FilteredCount == 0;
    public bool HasClosureReasons => ClosureReasonChips.Count > 0;
    public bool AllSelected => FilteredOps.Count > 0 && FilteredOps.All(r => r.IsSelected);
    public double TotalSelectedArea => FilteredOps.Where(r => r.IsSelected).Sum(r => r.Op.ComputedArea);
    public string TotalSelectedAreaDisplay => OpsParser.FormatArea(TotalSelectedArea);
    public string SelectAllLabel => AllSelected && FilteredCount > 0
        ? $"Deselect all ({SelectedCount}/{FilteredCount})"
        : $"Select all ({SelectedCount}/{FilteredCount})";

    public void LoadFromJson(string json, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rawOps = OpsParser.ParseOps(doc.RootElement);
            _allOps = rawOps.Select((op, i) => OpsParser.ProcessOps(op, i)).ToList();

            UploadedFileName = fileName;
            SearchQuery = "";
            ActiveOp = null;
            ErrorMessage = null;

            var overall = OpsParser.GetOverallTimeRange(_allOps);
            TimeframeFrom = overall?.from;
            TimeframeTo = overall?.to;

            RebuildClosureReasonChips();
            RefreshFilteredOps();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _allOps = new List<ParsedOps>();
            RebuildClosureReasonChips();
            RefreshFilteredOps();
        }
    }

    private void RebuildClosureReasonChips()
    {
        var previouslySelected = ClosureReasonChips.Where(c => c.IsSelected).Select(c => c.Reason).ToHashSet();
        var reasons = _allOps
            .Select(o => o.ClosureReason)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        var chips = new ObservableCollection<ClosureReasonChipViewModel>();
        foreach (var reason in reasons)
        {
            var chip = new ClosureReasonChipViewModel(reason!) { IsSelected = previouslySelected.Contains(reason!) };
            chip.PropertyChanged += OnClosureReasonChipChanged;
            chips.Add(chip);
        }
        ClosureReasonChips = chips;
        OnPropertyChanged(nameof(HasClosureReasons));
    }

    private void OnClosureReasonChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClosureReasonChipViewModel.IsSelected)) RefreshFilteredOps();
    }

    partial void OnSearchQueryChanged(string value) => RefreshFilteredOps();
    partial void OnTimeframeFromChanged(DateTimeOffset? value) => RefreshFilteredOps();
    partial void OnTimeframeToChanged(DateTimeOffset? value) => RefreshFilteredOps();

    private void RefreshFilteredOps()
    {
        IEnumerable<ParsedOps> result = _allOps;

        if (TimeframeFrom.HasValue)
        {
            var fromDate = TimeframeFrom.Value;
            var startOfDay = new DateTimeOffset(fromDate.Year, fromDate.Month, fromDate.Day, 0, 0, 0, TimeSpan.Zero);
            var toDate = TimeframeTo ?? TimeframeFrom.Value;
            var endOfDay = new DateTimeOffset(toDate.Year, toDate.Month, toDate.Day, 23, 59, 59, TimeSpan.Zero).AddMilliseconds(999);
            result = result.Where(op => op.StartTime <= endOfDay && op.EndTime >= startOfDay);
        }

        var selectedReasons = ClosureReasonChips.Where(c => c.IsSelected).Select(c => c.Reason).ToHashSet();
        if (selectedReasons.Count > 0)
        {
            result = result.Where(op => op.ClosureReason != null && selectedReasons.Contains(op.ClosureReason));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery.Trim().ToLowerInvariant();
            result = result.Where(op =>
                (op.Title?.ToLowerInvariant().Contains(query) ?? false) ||
                op.OperationPlanId.ToLowerInvariant().Contains(query) ||
                (op.Operator?.ToLowerInvariant().Contains(query) ?? false) ||
                (op.Description?.ToLowerInvariant().Contains(query) ?? false));
        }

        var previouslySelected = FilteredOps.Where(r => r.IsSelected).Select(r => r.OperationPlanId).ToHashSet();
        var activeOpId = ActiveOp?.OperationPlanId;

        var newRows = new ObservableCollection<OpsRowViewModel>();
        foreach (var op in result)
        {
            var row = new OpsRowViewModel(op) { IsSelected = previouslySelected.Contains(op.OperationPlanId) };
            row.PropertyChanged += OnRowPropertyChanged;
            newRows.Add(row);
        }
        FilteredOps = newRows;

        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasOps));
        OnPropertyChanged(nameof(HasFilteredOps));
        OnPropertyChanged(nameof(HasNoFilteredOps));
        RaiseSelectionChanged();

        ActiveOp = activeOpId != null ? FilteredOps.FirstOrDefault(r => r.OperationPlanId == activeOpId) : null;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OpsRowViewModel.IsSelected)) RaiseSelectionChanged();
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

    [RelayCommand]
    private void ActivateOp(OpsRowViewModel row) => ActiveOp = ReferenceEquals(ActiveOp, row) ? null : row;

    [RelayCommand]
    private void CloseDetails() => ActiveOp = null;

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var selectAll = !AllSelected;
        foreach (var row in FilteredOps) row.IsSelected = selectAll;
    }

    [RelayCommand]
    private void DeleteOp(OpsRowViewModel row)
    {
        _allOps.RemoveAll(o => o.OperationPlanId == row.OperationPlanId);
        RebuildClosureReasonChips();
        RefreshFilteredOps();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var selectedIds = FilteredOps.Where(r => r.IsSelected).Select(r => r.OperationPlanId).ToHashSet();
        if (selectedIds.Count == 0) return;
        _allOps.RemoveAll(o => selectedIds.Contains(o.OperationPlanId));
        RebuildClosureReasonChips();
        RefreshFilteredOps();
    }

    [RelayCommand]
    private void ClearClosureReasons()
    {
        foreach (var chip in ClosureReasonChips) chip.IsSelected = false;
    }

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
        FilteredOps.Where(r => r.IsSelected).SelectMany(r => r.Op.OperationVolumes.Select(v => new OpsExportRow
        {
            OperationPlanId = r.Op.OperationPlanId,
            Operator = r.Op.Operator,
            Title = r.Op.Title,
            State = r.Op.State,
            ClosureReason = r.Op.ClosureReason,
            SubmitTime = r.Op.SubmitTime,
            UpdateTime = r.Op.UpdateTime,
            TimeBegin = v.EffectiveTimeBegin,
            TimeEnd = v.EffectiveTimeEnd,
            ActualTimeEnd = v.ActualTimeEnd,
            MinAltitudeValue = v.MinAltitude?.AltitudeValue,
            MinAltitudeType = v.MinAltitude?.VerticalReference,
            MinAltitudeUnit = v.MinAltitude?.UnitsOfMeasure,
            MaxAltitudeValue = v.MaxAltitude?.AltitudeValue,
            MaxAltitudeType = v.MaxAltitude?.VerticalReference,
            MaxAltitudeUnit = v.MaxAltitude?.UnitsOfMeasure,
            AreaM2 = r.Op.ComputedArea,
            AreaKm2 = r.Op.ComputedArea / 1_000_000,
            Color = r.Op.Color,
        })).ToList();

    public string ExportSelectedToJson()
    {
        var rows = BuildExportRows();
        var exportObject = new { comment = $"Total number of ops: {rows.Count}", data = rows };
        return JsonSerializer.Serialize(exportObject, new JsonSerializerOptions { WriteIndented = true });
    }

    public byte[] ExportSelectedToXlsxBytes()
    {
        var rows = BuildExportRows();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("OPS");
        sheet.Cell(1, 1).InsertTable(rows);
        sheet.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
