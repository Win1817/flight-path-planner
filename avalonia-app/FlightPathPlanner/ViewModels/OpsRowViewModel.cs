using CommunityToolkit.Mvvm.ComponentModel;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public partial class OpsRowViewModel(ParsedOps op, Action<OpsRowViewModel, bool>? onSelectionChanged = null, bool selected = false) : ViewModelBase
{
    private bool _suppressNotification;

    public ParsedOps Op { get; } = op;

    /// <summary>A view of the tab's selection set (rows are created on demand and discarded on refilter).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_suppressNotification) onSelectionChanged?.Invoke(this, value);
    }

    /// <summary>Updates the checkbox without echoing back to the selection set (used by "select all").</summary>
    public void SetSelectedSilently(bool value)
    {
        _suppressNotification = true;
        IsSelected = value;
        _suppressNotification = false;
    }

    public string OperationPlanId => Op.OperationPlanId;
    public string Title => string.IsNullOrEmpty(Op.Title) ? "Untitled Operation" : Op.Title;
    public string? Description => Op.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Op.Description);
    public string Status => OpsParser.GetOperationStatus(Op.StartTime, Op.EndTime);
    public bool IsActiveStatus => Status == "active";
    public bool IsPendingStatus => Status == "pending";
    public bool IsExpiredStatus => Status == "expired";
    public string StatusLabel => string.IsNullOrEmpty(Status) ? "" : char.ToUpperInvariant(Status[0]) + Status[1..];
    public string StartTimeDisplay => OpsParser.FormatDateTimeShort(Op.StartTime);
    public string AreaDisplay => OpsParser.FormatArea(Op.ComputedArea);
    public int ZoneCount => Op.ZoneCount;
    public string? Color => Op.Color;

    // --- Details panel ---
    public string? Operator => Op.Operator;
    public bool HasOperator => !string.IsNullOrEmpty(Op.Operator);
    public string? State => Op.State;
    public string? ClosureReason => Op.ClosureReason;
    public bool HasState => !string.IsNullOrEmpty(Op.State);
    public bool HasClosureReason => !string.IsNullOrEmpty(Op.ClosureReason);
    public string ZonesDisplay => Op.ZoneCount == 1 ? "1 zone" : $"{Op.ZoneCount} zones";
    public bool HasStatusInfo => !string.IsNullOrEmpty(Op.State) || !string.IsNullOrEmpty(Op.ClosureReason);
    public string StateAndClosureReasonDisplay => string.Join(" / ", new[] { Op.State, Op.ClosureReason }.Where(s => !string.IsNullOrEmpty(s)));
    public string StartTimeFullDisplay => OpsParser.FormatDateTime(Op.StartTime);
    public string EndTimeFullDisplay => OpsParser.FormatDateTime(Op.EndTime);
    public string AreaM2Display => Op.ComputedArea.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " m²";

    // --- Schema (ED-269 / ED-318) ---
    public bool IsEd318 => Op.Schema == OpsSchema.Ed318;
    public string SchemaLabel => IsEd318 ? "ED-318" : "ED-269";
    public bool HasProviderId => !string.IsNullOrEmpty(Op.ProviderId);
    public string? ProviderId => Op.ProviderId;

    // --- ED-318 status: approval, take-off clearance and airspace conflicts have no ED-269 equivalent
    // (ED-269 only has the single free-text State/ClosureReason shown above), so these are only ever
    // populated on an ED-318 plan.
    public bool HasApproval => Op.Approval != null;
    public string ApprovalDisplay => Op.Approval?.State ?? "";
    public bool HasTakeoffClearance => Op.TakeoffClearance != null;
    public string TakeoffClearanceDisplay => Op.TakeoffClearance?.State ?? "";
    private static bool IsGranted(string? state) => string.Equals(state, "GRANTED", StringComparison.OrdinalIgnoreCase);
    public bool ApprovalGranted => IsGranted(Op.Approval?.State);
    public bool ClearanceGranted => IsGranted(Op.TakeoffClearance?.State);

    public int UnresolvedConflictCount => Op.Conflicts.Count(c => !c.Resolved);
    public bool HasUnresolvedConflicts => UnresolvedConflictCount > 0;
    public string ConflictsDisplay => Op.Conflicts.Count == 0 ? "" :
        HasUnresolvedConflicts ? $"{UnresolvedConflictCount} unresolved conflict{(UnresolvedConflictCount == 1 ? "" : "s")}" : $"{Op.Conflicts.Count} conflict{(Op.Conflicts.Count == 1 ? "" : "s")} (resolved)";
    public bool HasEd318StatusInfo => IsEd318 && (HasApproval || HasTakeoffClearance || Op.Conflicts.Count > 0);

    private (double min, double max, string unit)? AltitudeRange()
    {
        var altitudes = Op.AllVolumes
            .SelectMany(v => new[] { v.MinAltitude, v.MaxAltitude })
            .Where(a => a != null)
            .Select(a => a!)
            .ToList();
        if (altitudes.Count == 0) return null;
        var unit = altitudes.FirstOrDefault(a => !string.IsNullOrEmpty(a.UnitsOfMeasure))?.UnitsOfMeasure ?? "FT";
        return (altitudes.Min(a => a.AltitudeValue), altitudes.Max(a => a.AltitudeValue), unit);
    }

    public bool HasAltitudeInfo => AltitudeRange() != null;
    public string AltitudeRangeDisplay => AltitudeRange() is { } r
        ? $"{r.min:0.##} {r.unit} – {r.max:0.##} {r.unit}"
        : "—";
}
