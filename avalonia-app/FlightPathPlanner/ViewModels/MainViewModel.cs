using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;
using NetTopologySuite.Geometries;

namespace FlightPathPlanner.ViewModels;

public enum AppTab
{
    Ops,
    Aors,
    Report,
    Lookup,
    Saved,
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
    public SavedTabViewModel SavedTab { get; }
    public ToastCenterViewModel Toasts { get; } = new();

    private string? _hoveredId;

    /// <summary>The set of shapes changed (import, filter, tab switch...): rebuild the map data and move the camera to fit it.</summary>
    public event Action? MapDataInvalidated;

    /// <summary>Only the selection changed: rebuild the map data (selected shapes are flagged in it) but leave the camera alone.</summary>
    public event Action? MapSelectionChanged;

    /// <summary>Only hover/active items changed: cheap, just re-send the small highlight id list.</summary>
    public event Action? MapHighlightsInvalidated;

    /// <summary>The map reports a new visible area and the current data set is large enough to be drawn by viewport.</summary>
    public event Action? MapViewportChanged;

    private Envelope? _viewport;

    public MainViewModel() : this(LocalStorageService.CreateDefault()) { }

    /// <param name="storage">Where uploads/exports are kept; null disables local saving (used by tests).</param>
    public MainViewModel(LocalStorageService? storage)
    {
        OpsTab = new OpsTabViewModel(storage);
        AorTab = new AorTabViewModel(storage);
        ReportTab = new ReportTabViewModel(OpsTab, AorTab, storage);
        LookupTab = new LookupTabViewModel(OpsTab);
        SavedTab = new SavedTabViewModel(OpsTab.SavedFiles, AorTab.SavedFiles, ReportTab.SavedFiles);

        // Reloading a saved file jumps to the tab that now shows it.
        OpsTab.SavedFiles.Loaded += () => ActiveTab = AppTab.Ops;
        AorTab.SavedFiles.Loaded += () => ActiveTab = AppTab.Aors;

        foreach (var source in new INotifyPropertyChanged[] { OpsTab, AorTab, ReportTab, LookupTab, SavedTab })
        {
            source.PropertyChanged += (_, _) => RaiseChrome();
        }

        OpsTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Ops,
            data: new[] { nameof(OpsTabViewModel.FilteredOps) },
            selection: new[] { nameof(OpsTabViewModel.SelectedCount) },
            highlights: new[] { nameof(OpsTabViewModel.ActiveOp) });
        AorTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Aors,
            data: new[] { nameof(AorTabViewModel.FilteredAors) },
            selection: new[] { nameof(AorTabViewModel.SelectedCount) },
            highlights: new[] { nameof(AorTabViewModel.ActiveAor) });
        // Report and lookup draw a handful of shapes, so any change to them rebuilds the whole (small) set.
        ReportTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Report,
            data: new[] { nameof(ReportTabViewModel.MatchingOps), nameof(ReportTabViewModel.SelectedAor) }, selection: Array.Empty<string>(), highlights: Array.Empty<string>());
        LookupTab.PropertyChanged += (_, e) => OnTabChanged(e, AppTab.Lookup,
            data: new[] { nameof(LookupTabViewModel.MatchingOps), nameof(LookupTabViewModel.Center), nameof(LookupTabViewModel.RadiusKm) },
            selection: Array.Empty<string>(), highlights: Array.Empty<string>());
    }

    private void OnTabChanged(PropertyChangedEventArgs e, AppTab tab, string[] data, string[] selection, string[] highlights)
    {
        bool shown = ActiveTab == tab || (tab == AppTab.Ops && ActiveTab == AppTab.Saved);
        var name = e.PropertyName;
        if (name == null) return;

        if (data.Contains(name))
        {
            if (shown) MapDataInvalidated?.Invoke();
        }
        else if (selection.Contains(name))
        {
            if (shown) MapSelectionChanged?.Invoke();
        }
        else if (highlights.Contains(name))
        {
            if (shown) MapHighlightsInvalidated?.Invoke();
        }
        else if (name == nameof(AorTabViewModel.AllAors) && ActiveTab == AppTab.Report)
        {
            MapDataInvalidated?.Invoke(); // the report shows every AoR until one is picked
        }
    }

    partial void OnActiveTabChanged(AppTab value)
    {
        OnPropertyChanged(nameof(IsOpsActive));
        OnPropertyChanged(nameof(IsAorsActive));
        OnPropertyChanged(nameof(IsReportActive));
        OnPropertyChanged(nameof(IsLookupActive));
        OnPropertyChanged(nameof(IsSavedActive));
        RaiseChrome();
        MapDataInvalidated?.Invoke();
    }

    // ---- Navigation / header state (all derived from real data) ----
    public bool IsOpsActive => ActiveTab == AppTab.Ops;
    public bool IsAorsActive => ActiveTab == AppTab.Aors;
    public bool IsReportActive => ActiveTab == AppTab.Report;
    public bool IsLookupActive => ActiveTab == AppTab.Lookup;
    public bool IsSavedActive => ActiveTab == AppTab.Saved;

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public string OpsBadge => OpsTab.HasOps ? N(OpsTab.FilteredCount) : "";
    public bool HasOpsBadge => OpsTab.HasOps;
    public string AorBadge => AorTab.HasAors ? N(AorTab.FilteredCount) : "";
    public bool HasAorBadge => AorTab.HasAors;
    public string SavedBadge => N(SavedTab.TotalCount);
    public bool HasSavedBadge => SavedTab.TotalCount > 0;

    public string OpsPillText => $"{N(OpsTab.TotalCount)} OPS";
    public string AorPillText => $"{N(AorTab.TotalCount)} AoRs";
    public bool HasAnyData => OpsTab.HasOps || AorTab.HasAors;

    public string SectionTitle => ActiveTab switch
    {
        AppTab.Ops => "Operations",
        AppTab.Aors => "Areas of Responsibility",
        AppTab.Report => "Report",
        AppTab.Lookup => "Flight Lookup",
        _ => "Saved Locally",
    };

    public string SectionSubtitle => ActiveTab switch
    {
        AppTab.Ops => !OpsTab.HasOps
            ? "No operations loaded"
            : OpsTab.FilteredCount == OpsTab.TotalCount
                ? $"{N(OpsTab.TotalCount)} operations loaded"
                : $"{N(OpsTab.FilteredCount)} of {N(OpsTab.TotalCount)} operations shown",
        AppTab.Aors => !AorTab.HasAors
            ? "No areas loaded"
            : AorTab.FilteredCount == AorTab.TotalCount
                ? $"{N(AorTab.TotalCount)} areas loaded"
                : $"{N(AorTab.FilteredCount)} of {N(AorTab.TotalCount)} areas shown",
        AppTab.Report => !ReportTab.HasOpsAndAors
            ? "Load OPS and AoR data to build a report"
            : ReportTab.SelectedAor is { } aor
                ? $"{aor.Name} · {N(ReportTab.MatchCount)} matching OPS"
                : $"{N(ReportTab.AorCount)} AoRs available",
        AppTab.Lookup => LookupTab.Center is { } center
            ? $"{center.DisplayName} · {N(LookupTab.MatchCount)} OPS within {LookupTab.RadiusKm:0.##} km"
            : "Search an address or coordinates",
        _ => SavedTab.TotalCount == 1 ? "1 file kept locally" : $"{N(SavedTab.TotalCount)} files kept locally",
    };

    private void RaiseChrome()
    {
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionSubtitle));
        OnPropertyChanged(nameof(OpsBadge));
        OnPropertyChanged(nameof(HasOpsBadge));
        OnPropertyChanged(nameof(AorBadge));
        OnPropertyChanged(nameof(HasAorBadge));
        OnPropertyChanged(nameof(SavedBadge));
        OnPropertyChanged(nameof(HasSavedBadge));
        OnPropertyChanged(nameof(OpsPillText));
        OnPropertyChanged(nameof(AorPillText));
        OnPropertyChanged(nameof(HasAnyData));
    }

    [RelayCommand]
    private void SelectTab(AppTab tab) => ActiveTab = tab;

    // =====================================================================================================
    //  Map
    // =====================================================================================================

    /// <summary>Whether the current map content is large enough that it is drawn by viewport rather than all at once.</summary>
    public bool IsMapViewportMode => ActiveTab switch
    {
        AppTab.Ops or AppTab.Saved => MapPayloadBuilder.UsesViewport(OpsTab.FilteredCount),
        AppTab.Aors => MapPayloadBuilder.UsesViewport(AorTab.FilteredCount),
        AppTab.Report => ReportTab.SelectedAor == null && MapPayloadBuilder.UsesViewport(AorTab.TotalCount),
        _ => false,
    };

    /// <summary>Called with the area the map page currently shows (west, south, east, north).</summary>
    public void HandleMapViewportChanged(double west, double south, double east, double north)
    {
        var next = new Envelope(west, east, south, north);
        if (_viewport != null && _viewport.Equals(next)) return;
        _viewport = next;
        if (IsMapViewportMode) MapViewportChanged?.Invoke();
    }

    /// <summary>Snapshots what the map should show (cheap; UI thread) and returns a builder that turns the snapshot into a payload. The
    /// builder only touches immutable data (dataset arrays, filter masks, copied id sets), so it is safe to run on a worker thread.</summary>
    public Func<CancellationToken, MapPayload> CreateMapRequest(bool fit, int version)
    {
        var viewport = _viewport;
        switch (ActiveTab)
        {
            case AppTab.Ops:
            case AppTab.Saved:
            {
                var dataset = OpsTab.Dataset;
                var mask = OpsTab.FilterMask;
                int included = OpsTab.FilteredCount;
                var highlighted = OpsTab.SelectedOps.Select(o => o.OperationPlanId).ToHashSet();
                return ct => MapPayloadBuilder.ForOps(dataset, mask, included, highlighted, viewport, fit, version, ct);
            }
            case AppTab.Aors:
            {
                var dataset = AorTab.Dataset;
                var mask = AorTab.FilterMask;
                int included = AorTab.FilteredCount;
                var highlighted = AorTab.SelectedAors.Select(a => a.Id).ToHashSet();
                return ct => MapPayloadBuilder.ForAors(dataset, mask, included, highlighted, viewport, fit, version, ct);
            }
            case AppTab.Report when ReportTab.SelectedAor is { } selected:
            {
                var ops = ReportTab.MatchingOps.Select(r => r.Op).ToArray();
                var aors = new[] { selected.Aor };
                var highlighted = ops.Select(o => o.OperationPlanId).Append(selected.Aor.Id).ToHashSet();
                return ct => MapPayloadBuilder.ForSmallSet(ops, aors, null, highlighted, fit, version, ct);
            }
            case AppTab.Report:
            {
                var dataset = AorTab.Dataset;
                int included = AorTab.TotalCount;
                var none = new HashSet<string>();
                return ct => MapPayloadBuilder.ForAors(dataset, null, included, none, viewport, fit, version, ct);
            }
            default:
            {
                if (LookupTab.Center is not { } center)
                    return ct => MapPayloadBuilder.ForSmallSet(Array.Empty<ParsedOps>(), Array.Empty<ParsedAor>(), null, new HashSet<string>(), fit, version, ct);
                var ops = LookupTab.MatchingOps.Select(r => r.Op).ToArray();
                var radius = (center.Lng, center.Lat, LookupTab.RadiusKm);
                var highlighted = ops.Select(o => o.OperationPlanId).ToHashSet();
                return ct => MapPayloadBuilder.ForSmallSet(ops, Array.Empty<ParsedAor>(), radius, highlighted, fit, version, ct);
            }
        }
    }

    /// <summary>The whole current map content as one GeoJSON FeatureCollection. For tests and small inputs only: the app draws through
    /// <see cref="CreateMapRequest"/>, which never builds a collection for a whole large dataset.</summary>
    public string BuildMapGeoJson()
    {
        switch (ActiveTab)
        {
            case AppTab.Saved:
            case AppTab.Ops:
                return MapGeoJson.Build(OpsTab.FilteredOpData, Array.Empty<ParsedAor>(), null, OpsTab.SelectedOps.Select(o => o.OperationPlanId).ToHashSet());
            case AppTab.Aors:
                return MapGeoJson.Build(Array.Empty<ParsedOps>(), AorTab.FilteredAorData, null, AorTab.SelectedAors.Select(a => a.Id).ToHashSet());
            case AppTab.Report:
                if (ReportTab.SelectedAor is { } selected)
                {
                    var ops = ReportTab.MatchingOps.Select(r => r.Op).ToArray();
                    return MapGeoJson.Build(ops, new[] { selected.Aor }, null, ops.Select(o => o.OperationPlanId).Append(selected.Aor.Id).ToHashSet());
                }
                return MapGeoJson.Build(Array.Empty<ParsedOps>(), AorTab.AllAors);
            default:
                if (LookupTab.Center is { } center)
                {
                    var ops = LookupTab.MatchingOps.Select(r => r.Op).ToArray();
                    return MapGeoJson.Build(ops, Array.Empty<ParsedAor>(), (center.Lng, center.Lat, LookupTab.RadiusKm), ops.Select(o => o.OperationPlanId).ToHashSet());
                }
                return MapGeoJson.Build(Array.Empty<ParsedOps>(), Array.Empty<ParsedAor>());
        }
    }

    /// <summary>Ids to emphasise on top of the selection flags carried in the map data: the hovered and the active item (at most two).</summary>
    public IReadOnlyCollection<string> BuildHighlightIds()
    {
        var ids = new HashSet<string>();
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
                AorTab.ActiveAor = AorTab.FindVisibleRow(id) ?? AorTab.ActiveAor;
        }
        else
        {
            var row = OpsTab.FindVisibleRow(id);
            if (row != null) OpsTab.ActiveOp = row;
            if (ActiveTab == AppTab.Aors) ActiveTab = AppTab.Ops;
        }
        MapHighlightsInvalidated?.Invoke();
    }
}
