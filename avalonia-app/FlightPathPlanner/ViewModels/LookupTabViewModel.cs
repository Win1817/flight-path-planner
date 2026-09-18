using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

/// <summary>Mirrors src/components/FlightLookup.tsx + src/pages/Index.tsx's lookup state:
/// resolve a free-text query (coordinates or address) to a center point via GeocodeService,
/// then radius-search the OPS tab's currently filtered ops around it via ReportService.</summary>
public partial class LookupTabViewModel : ViewModelBase
{
    public static readonly double[] RadiusPresetsKm = { 0.5, 1, 2, 5, 10 };

    private readonly OpsTabViewModel _opsTab;
    private readonly HttpClient _httpClient;

    public LookupTabViewModel(OpsTabViewModel opsTab) : this(opsTab, new HttpClient()) { }

    public LookupTabViewModel(OpsTabViewModel opsTab, HttpClient httpClient)
    {
        _opsTab = opsTab;
        _httpClient = httpClient;
        _opsTab.PropertyChanged += OnOpsTabPropertyChanged;
    }

    public ObservableCollection<RadiusPresetOptionViewModel> RadiusPresets { get; } =
        new(RadiusPresetsKm.Select(km => new RadiusPresetOptionViewModel(km)));

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial double RadiusKm { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial GeocodeResult? Center { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<GeocodeResult> Candidates { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<OpsRowViewModel> MatchingOps { get; set; } = new();

    public bool HasOps => _opsTab.HasOps;
    public bool HasCandidates => Candidates.Count > 0;
    public bool HasCenter => Center != null;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool CanSearch => !IsLoading && !string.IsNullOrWhiteSpace(Query);
    public int MatchCount => MatchingOps.Count;
    public bool HasMatches => MatchCount > 0;
    public bool HasNoMatches => HasCenter && MatchCount == 0;
    public string CenterCoordinatesDisplay => Center != null ? $"{Center.Lat:F5}, {Center.Lng:F5}" : "";
    public string RadiusSummaryDisplay => $"flight plans within {RadiusKm:0.#}km";

    public string RadiusKmText
    {
        get => RadiusKm.ToString("0.#", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                RadiusKm = Math.Max(0.1, parsed);
            }
        }
    }

    partial void OnQueryChanged(string value) => OnPropertyChanged(nameof(CanSearch));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnCenterChanged(GeocodeResult? value)
    {
        OnPropertyChanged(nameof(HasCenter));
        OnPropertyChanged(nameof(CenterCoordinatesDisplay));
    }

    partial void OnRadiusKmChanged(double value)
    {
        OnPropertyChanged(nameof(RadiusSummaryDisplay));
        OnPropertyChanged(nameof(RadiusKmText));
        RefreshMatches();
    }

    private void OnOpsTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpsTabViewModel.FilteredOps) or nameof(OpsTabViewModel.HasOps))
        {
            OnPropertyChanged(nameof(HasOps));
            RefreshMatches();
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var trimmed = Query.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        ErrorMessage = null;
        Candidates = new ObservableCollection<GeocodeResult>();
        OnPropertyChanged(nameof(HasCandidates));
        IsLoading = true;
        try
        {
            var results = await GeocodeService.ResolveLocationAsync(_httpClient, trimmed);
            if (results.Count == 0)
            {
                ErrorMessage = "No location found for that search.";
            }
            else if (results.Count == 1)
            {
                SelectCandidate(results[0]);
            }
            else
            {
                Candidates = new ObservableCollection<GeocodeResult>(results);
                OnPropertyChanged(nameof(HasCandidates));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectCandidate(GeocodeResult result)
    {
        Center = result;
        Candidates = new ObservableCollection<GeocodeResult>();
        OnPropertyChanged(nameof(HasCandidates));
        RefreshMatches();
    }

    public void SelectRadiusPreset(double km) => RadiusKm = km;

    [RelayCommand]
    private void Clear()
    {
        Center = null;
        Candidates = new ObservableCollection<GeocodeResult>();
        OnPropertyChanged(nameof(HasCandidates));
        ErrorMessage = null;
        Query = "";
        RefreshMatches();
    }

    private void RefreshMatches()
    {
        if (Center == null)
        {
            MatchingOps = new ObservableCollection<OpsRowViewModel>();
        }
        else
        {
            var currentOps = _opsTab.FilteredOps.Select(r => r.Op).ToList();
            var matched = ReportService.GetOpsNearPoint(currentOps, Center.Lng, Center.Lat, RadiusKm);
            MatchingOps = new ObservableCollection<OpsRowViewModel>(matched.Select(op => new OpsRowViewModel(op)));
        }
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private sealed class LookupExportRow
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
        public double? DistanceFromSearchCenterKm { get; init; }
        public string? Color { get; init; }
    }

    private List<LookupExportRow> BuildExportRows()
    {
        if (Center == null) return new List<LookupExportRow>();
        var center = Center;

        return MatchingOps.SelectMany(r => r.Op.OperationVolumes.Select(v =>
        {
            double? distanceKm = null;
            if (v.OperationGeography != null)
            {
                var centroid = GeoMath.Centroid(v.OperationGeography.Polygons);
                distanceKm = Math.Round(GeoMath.DistanceKm(center.Lng, center.Lat, centroid[0], centroid[1]) * 100) / 100;
            }

            return new LookupExportRow
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
                DistanceFromSearchCenterKm = distanceKm,
                Color = r.Op.Color,
            };
        })).ToList();
    }

    public string ExportMatchesToJson()
    {
        var rows = BuildExportRows();
        var comment = Center != null
            ? $"{MatchingOps.Count} flight plan(s) within {RadiusKm}km of {Center.DisplayName}"
            : "";
        var exportObject = new { comment, data = rows };
        return JsonSerializer.Serialize(exportObject, new JsonSerializerOptions { WriteIndented = true });
    }

    public byte[] ExportMatchesToXlsxBytes()
    {
        var rows = BuildExportRows();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Flight Lookup");
        sheet.Cell(1, 1).InsertTable(rows);
        sheet.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
