using CommunityToolkit.Mvvm.ComponentModel;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public partial class AorRowViewModel(ParsedAor aor, Action<AorRowViewModel, bool>? onSelectionChanged = null, bool selected = false) : ViewModelBase
{
    private bool _suppressNotification;

    public ParsedAor Aor { get; } = aor;

    /// <summary>Selection lives in the tab's selection set (rows are created on demand and discarded on refilter), so this
    /// property is just a view of it: user changes flow to the set through the callback.</summary>
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

    public string Id => Aor.Id;
    public string Name => Aor.Name;
    public string Designator => Aor.Designator;
    public string? Color => Aor.Color;
    public string AreaDisplay => OpsParser.FormatArea(Aor.ComputedArea);

    // --- Details panel ---
    public string? Restriction => Aor.Restriction;
    public bool IsProhibited => Aor.Restriction == "PROHIBITED";
    public bool IsNoRestriction => Aor.Restriction == "NO_RESTRICTION";
    public bool IsConditionalRestriction => !string.IsNullOrEmpty(Aor.Restriction) && !IsProhibited && !IsNoRestriction;
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
