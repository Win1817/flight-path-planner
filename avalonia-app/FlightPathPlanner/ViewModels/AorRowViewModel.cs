using CommunityToolkit.Mvvm.ComponentModel;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public partial class AorRowViewModel(ParsedAor aor) : ViewModelBase
{
    public ParsedAor Aor { get; } = aor;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Id => Aor.Id;
    public string Name => Aor.Name;
    public string Designator => Aor.Designator;
    public string? Color => Aor.Color;
    public string AreaDisplay => OpsParser.FormatArea(Aor.ComputedArea);

    // --- Details panel ---
    public string? Restriction => Aor.Restriction;
    public bool HasRestriction => !string.IsNullOrEmpty(Aor.Restriction);
    public string RestrictionDisplay => (Aor.Restriction ?? "").Replace('_', ' ');
    public string? Message => Aor.Message;
    public bool HasMessage => !string.IsNullOrEmpty(Aor.Message);
    public bool HasApplicability => !string.IsNullOrEmpty(Aor.EffectiveTimeBegin) || !string.IsNullOrEmpty(Aor.EffectiveTimeEnd);
    public bool HasEffectiveTimeBegin => !string.IsNullOrEmpty(Aor.EffectiveTimeBegin);
    public bool HasEffectiveTimeEnd => !string.IsNullOrEmpty(Aor.EffectiveTimeEnd);
    public string EffectiveTimeBeginDisplay => FormatDateTimeString(Aor.EffectiveTimeBegin);
    public string EffectiveTimeEndDisplay => FormatDateTimeString(Aor.EffectiveTimeEnd);

    private static string FormatDateTimeString(string? value) =>
        !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? OpsParser.FormatDateTime(parsed)
            : "—";
    public string LowerLimitDisplay => $"{Aor.LowerLimit:0.##} {Aor.VerticalLimitsUom}";
    public string UpperLimitDisplay => $"{Aor.UpperLimit:0.##} {Aor.VerticalLimitsUom}";
    public string VerticalReferenceType => Aor.VerticalReferenceType;
    public bool HasAutoReject => Aor.AutoReject.HasValue;
    public string AutoRejectDisplay => Aor.AutoReject == true ? "ON" : "OFF";
}
