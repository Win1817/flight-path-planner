using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public enum AppTab
{
    Ops,
    Aors,
    Report,
    Lookup,
}

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial AppTab ActiveTab { get; set; } = AppTab.Ops;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public OpsTabViewModel OpsTab { get; }
    public AorTabViewModel AorTab { get; }
    public ReportTabViewModel ReportTab { get; }
    public LookupTabViewModel LookupTab { get; }

    private string? _hoveredId;

    /// <summary>Raised when the set of shapes on the map changed (view should call <see cref="BuildMapGeoJson"/>).</summary>
    public event Action? MapDataInvalidated;

    /// <summary>Raised when only the highlighted shapes changed (view should call <see cref="BuildHighlightIds"/>).</summary>
    public event Action? MapHighlightsInvalidated;

    public MainViewModel() : this(LocalStorageService.CreateDefault()) { }

    /// <param name="storage">Where uploads/exports are kept; null disables local saving (used by tests).</param>
    public MainViewModel(LocalStorageService? storage)
    {
        OpsTab = new OpsTabViewModel(storage);
        AorTab = new AorTabViewModel(storage);
        ReportTab = new ReportTabViewModel(OpsTab, AorTab, storage);
        LookupTab = new LookupTabViewModel(OpsTab);

        OpsTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Ops,
            data: nameof(OpsTabViewModel.FilteredOps),
            highlights: new[] { nameof(OpsTabViewModel.SelectedCount), nameof(OpsTabViewModel.ActiveOp) });
        AorTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Aors,
            data: nameof(AorTabViewModel.FilteredAors),
            highlights: new[] { nameof(AorTabViewModel.SelectedCount), nameof(AorTabViewModel.ActiveAor) });
        ReportTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Report,
            data: nameof(ReportTabViewModel.MatchingOps), highlights: new[] { nameof(ReportTabViewModel.SelectedAor) });
        LookupTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Lookup,
            data: nameof(LookupTabViewModel.MatchingOps), highlights: new[] { nameof(LookupTabViewModel.Center), nameof(LookupTabViewModel.RadiusKm) });
    }

    private void OnTabChanged(PropertyChangedEventArgs e, AppTab tab, string data, string[] highlights)
    {
        if (e.PropertyName == data)
        {
            // The report also shows every AoR when none is picked, and the lookup circle depends on center/radius.
            if (ActiveTab == tab) MapDataInvalidated?.Invoke();
        }
        else if (e.PropertyName != null && highlights.Contains(e.PropertyName))
        {
            if (ActiveTab != tab) return;
            if (tab is AppTab.Report or AppTab.Lookup) MapDataInvalidated?.Invoke();
            else MapHighlightsInvalidated?.Invoke();
        }
        else if (e.PropertyName == nameof(AorTabViewModel.AllAors) && ActiveTab == AppTab.Report)
        {
            MapDataInvalidated?.Invoke();
        }
    }

    partial void OnActiveTabChanged(AppTab value)
    {
        MapDataInvalidated?.Invoke();
    }

    [RelayCommand]
    private void SelectTab(AppTab tab) => ActiveTab = tab;

    public string BuildMapGeoJson()
    {
        switch (ActiveTab)
        {
            case AppTab.Ops:
                return MapGeoJson.Build(OpsTab.FilteredOps.Select(r => r.Op), Array.Empty<ParsedAor>());
            case AppTab.Aors:
                return MapGeoJson.Build(Array.Empty<ParsedOps>(), AorTab.FilteredAors.Select(r => r.Aor));
            case AppTab.Report:
                if (ReportTab.SelectedAor is { } selected)
                    return MapGeoJson.Build(ReportTab.MatchingOps.Select(r => r.Op), new[] { selected.Aor });
                return MapGeoJson.Build(Array.Empty<ParsedOps>(), AorTab.AllAors);
            default:
                if (LookupTab.Center is { } center)
                    return MapGeoJson.Build(LookupTab.MatchingOps.Select(r => r.Op), Array.Empty<ParsedAor>(),
                        (center.Lng, center.Lat, LookupTab.RadiusKm));
                return MapGeoJson.Build(Array.Empty<ParsedOps>(), Array.Empty<ParsedAor>());
        }
    }

    public IReadOnlyCollection<string> BuildHighlightIds()
    {
        var ids = new HashSet<string>();
        switch (ActiveTab)
        {
            case AppTab.Ops:
                foreach (var r in OpsTab.FilteredOps.Where(r => r.IsSelected)) ids.Add(r.OperationPlanId);
                break;
            case AppTab.Aors:
                foreach (var r in AorTab.FilteredAors.Where(r => r.IsSelected)) ids.Add(r.Id);
                break;
            case AppTab.Report:
                foreach (var r in ReportTab.MatchingOps) ids.Add(r.OperationPlanId);
                if (ReportTab.SelectedAor != null) ids.Add(ReportTab.SelectedAor.Id);
                break;
            default:
                foreach (var r in LookupTab.MatchingOps) ids.Add(r.OperationPlanId);
                break;
        }
        if (_hoveredId != null) ids.Add(_hoveredId);
        if (OpsTab.ActiveOp != null) ids.Add(OpsTab.ActiveOp.OperationPlanId);
        if (AorTab.ActiveAor != null) ids.Add(AorTab.ActiveAor.Id);
        return ids;
    }

    public void HandleMapZoneHovered(string? id)
    {
        _hoveredId = id;
        MapHighlightsInvalidated?.Invoke();
    }

    public void HandleMapZoneClicked(string id, string dataType)
    {
        if (id == MapGeoJson.LookupRadiusId) return;

        if (dataType == "aor")
        {
            if (ActiveTab == AppTab.Report)
                ReportTab.SelectedAor = ReportTab.AorOptions.FirstOrDefault(o => o.Id == id) ?? ReportTab.SelectedAor;
            else
                AorTab.ActiveAor = AorTab.FilteredAors.FirstOrDefault(r => r.Id == id) ?? AorTab.ActiveAor;
        }
        else
        {
            var row = OpsTab.FilteredOps.FirstOrDefault(r => r.OperationPlanId == id);
            if (row != null) OpsTab.ActiveOp = row;
            if (ActiveTab == AppTab.Aors) ActiveTab = AppTab.Ops;
        }
        MapHighlightsInvalidated?.Invoke();
    }
}
