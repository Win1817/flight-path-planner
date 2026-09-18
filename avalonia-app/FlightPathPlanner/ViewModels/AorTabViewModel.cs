using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

/// <summary>Mirrors src/pages/Index.tsx's AoR state/filtering (filteredAors useMemo chain) plus
/// AorList.tsx/AorDetails.tsx, adapted to MVVM.</summary>
public partial class AorTabViewModel : ViewModelBase
{
    private List<ParsedAor> _allAors = new();

    [ObservableProperty]
    public partial ObservableCollection<AorRowViewModel> FilteredAors { get; set; } = new();

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial AorRowViewModel? ActiveAor { get; set; }

    [ObservableProperty]
    public partial string? UploadedFileName { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public IReadOnlyList<ParsedAor> AllAors => _allAors;
    public int TotalCount => _allAors.Count;
    public int FilteredCount => FilteredAors.Count;
    public int SelectedCount => FilteredAors.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public bool HasAors => TotalCount > 0;
    public bool HasFilteredAors => FilteredCount > 0;
    public bool HasNoFilteredAors => HasAors && FilteredCount == 0;
    public bool AllSelected => FilteredAors.Count > 0 && FilteredAors.All(r => r.IsSelected);
    public double TotalSelectedArea => FilteredAors.Where(r => r.IsSelected).Sum(r => r.Aor.ComputedArea);
    public string TotalSelectedAreaDisplay => OpsParser.FormatArea(TotalSelectedArea);
    public string SelectAllLabel => AllSelected && FilteredCount > 0
        ? $"Deselect all ({SelectedCount}/{FilteredCount})"
        : $"Select all ({SelectedCount}/{FilteredCount})";

    public void LoadFromJson(string json, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rawAors = AorParser.ParseAors(doc.RootElement);
            _allAors = rawAors.Aors.Select((aor, i) => AorParser.ProcessAor(aor, i)).ToList();

            UploadedFileName = fileName;
            SearchQuery = "";
            ActiveAor = null;
            ErrorMessage = rawAors.Skipped > 0
                ? $"Loaded with {rawAors.Skipped} entr{(rawAors.Skipped == 1 ? "y" : "ies")} skipped (unrecognized format)."
                : null;

            RefreshFilteredAors();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _allAors = new List<ParsedAor>();
            RefreshFilteredAors();
        }
    }

    partial void OnSearchQueryChanged(string value) => RefreshFilteredAors();

    private void RefreshFilteredAors()
    {
        IEnumerable<ParsedAor> result = _allAors;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery.Trim().ToLowerInvariant();
            result = result.Where(aor =>
                (aor.Name?.ToLowerInvariant().Contains(query) ?? false) ||
                (aor.Designator?.ToLowerInvariant().Contains(query) ?? false) ||
                (aor.Id?.ToLowerInvariant().Contains(query) ?? false) ||
                (aor.Message?.ToLowerInvariant().Contains(query) ?? false) ||
                (aor.Restriction?.ToLowerInvariant().Contains(query) ?? false) ||
                (aor.Reasons?.Any(r => r.ToLowerInvariant().Contains(query)) ?? false));
        }

        var previouslySelected = FilteredAors.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet();
        var activeAorId = ActiveAor?.Id;

        var newRows = new ObservableCollection<AorRowViewModel>();
        foreach (var aor in result)
        {
            var row = new AorRowViewModel(aor) { IsSelected = previouslySelected.Contains(aor.Id) };
            row.PropertyChanged += OnRowPropertyChanged;
            newRows.Add(row);
        }
        FilteredAors = newRows;

        OnPropertyChanged(nameof(AllAors));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasAors));
        OnPropertyChanged(nameof(HasFilteredAors));
        OnPropertyChanged(nameof(HasNoFilteredAors));
        RaiseSelectionChanged();

        ActiveAor = activeAorId != null ? FilteredAors.FirstOrDefault(r => r.Id == activeAorId) : null;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AorRowViewModel.IsSelected)) RaiseSelectionChanged();
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
    private void ActivateAor(AorRowViewModel row) => ActiveAor = ReferenceEquals(ActiveAor, row) ? null : row;

    [RelayCommand]
    private void CloseDetails() => ActiveAor = null;

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var selectAll = !AllSelected;
        foreach (var row in FilteredAors) row.IsSelected = selectAll;
    }

    [RelayCommand]
    private void DeleteAor(AorRowViewModel row)
    {
        _allAors.RemoveAll(a => a.Id == row.Id);
        RefreshFilteredAors();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var selectedIds = FilteredAors.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet();
        if (selectedIds.Count == 0) return;
        _allAors.RemoveAll(a => selectedIds.Contains(a.Id));
        RefreshFilteredAors();
    }

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
        FilteredAors.Where(r => r.IsSelected).Select(r => new AorExportRow
        {
            Id = r.Aor.Id,
            Name = r.Aor.Name,
            Designator = r.Aor.Designator,
            LowerLimit = r.Aor.LowerLimit,
            UpperLimit = r.Aor.UpperLimit,
            VerticalLimitsUom = r.Aor.VerticalLimitsUom,
            VerticalReferenceType = r.Aor.VerticalReferenceType,
            AutoReject = r.Aor.AutoReject,
            AutoApprovalEnabled = r.Aor.AutoApprovalEnabled,
            AorEnabled = r.Aor.AorEnabled,
            AutoTakeOffClearanceEnabled = r.Aor.AutoTakeOffClearanceEnabled,
            MaxSimultaneousOperationsEnabled = r.Aor.MaxSimultaneousOperationsEnabled,
            MaxSimultaneousOperations = r.Aor.MaxSimultaneousOperations,
            FeatureType = r.Aor.FeatureType,
            AreaM2 = r.Aor.ComputedArea,
            AreaKm2 = r.Aor.ComputedArea / 1_000_000,
        }).ToList();

    public string ExportSelectedToJson()
    {
        var rows = BuildExportRows();
        var exportObject = new { comment = $"Total number of AoRs: {rows.Count}", data = rows };
        return JsonSerializer.Serialize(exportObject, new JsonSerializerOptions { WriteIndented = true });
    }

    public byte[] ExportSelectedToXlsxBytes()
    {
        var rows = BuildExportRows();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("AoRs");
        sheet.Cell(1, 1).InsertTable(rows);
        sheet.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
