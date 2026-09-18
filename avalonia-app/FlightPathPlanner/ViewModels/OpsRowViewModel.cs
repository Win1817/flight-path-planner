using CommunityToolkit.Mvvm.ComponentModel;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public partial class OpsRowViewModel(ParsedOps op) : ViewModelBase
{
    public ParsedOps Op { get; } = op;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string OperationPlanId => Op.OperationPlanId;
    public string Title => string.IsNullOrEmpty(Op.Title) ? "Untitled Operation" : Op.Title;
    public string? Description => Op.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Op.Description);
    public string Status => OpsParser.GetOperationStatus(Op.StartTime, Op.EndTime);
    public string StartTimeDisplay => OpsParser.FormatDateTimeShort(Op.StartTime);
    public string AreaDisplay => OpsParser.FormatArea(Op.ComputedArea);
    public int ZoneCount => Op.ZoneCount;
    public string? Color => Op.Color;

    // --- Details panel ---
    public string? Operator => Op.Operator;
    public bool HasOperator => !string.IsNullOrEmpty(Op.Operator);
    public string? State => Op.State;
    public string? ClosureReason => Op.ClosureReason;
    public bool HasStatusInfo => !string.IsNullOrEmpty(Op.State) || !string.IsNullOrEmpty(Op.ClosureReason);
    public string StateAndClosureReasonDisplay => string.Join(" / ", new[] { Op.State, Op.ClosureReason }.Where(s => !string.IsNullOrEmpty(s)));
    public string StartTimeFullDisplay => OpsParser.FormatDateTime(Op.StartTime);
    public string EndTimeFullDisplay => OpsParser.FormatDateTime(Op.EndTime);
    public string AreaM2Display => Op.ComputedArea.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " m²";

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
