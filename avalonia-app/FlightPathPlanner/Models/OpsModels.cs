namespace FlightPathPlanner.Models;

/// <summary>Which wire schema an OPS record was read from. The two aren't the same vocabulary — ED-269 plans carry a
/// single free-text <see cref="Ops.State"/>/<see cref="Ops.ClosureReason"/>, while ED-318 plans separately track
/// approval, take-off clearance and airspace conflicts (see <see cref="Ops.Approval"/>, <see cref="Ops.TakeoffClearance"/>,
/// <see cref="Ops.Conflicts"/>). Kept on the record so the UI never conflates the two.</summary>
public enum OpsSchema { Ed269, Ed318 }

public sealed class Altitude
{
    public double AltitudeValue { get; init; }
    public string UnitsOfMeasure { get; init; } = "FT";
    public string? VerticalReference { get; init; }
}

public sealed class Contact
{
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

/// <summary>The most recent local approval or take-off clearance result for an ED-318 plan (from
/// <c>localApprovalResults</c> / <c>localTakeoffClearanceResults</c> — each is a version history; only the latest,
/// by <c>updateTime</c>, is kept).</summary>
public sealed class ApprovalStatus
{
    public string? State { get; init; } // e.g. GRANTED, DENIED, PENDING
    public string? EvaluationType { get; init; } // e.g. AUTOMATIC, MANUAL
}

/// <summary>An airspace/authority conflict reported against an ED-318 plan (from <c>conflicts</c>).</summary>
public sealed class OpsConflict
{
    public string? Message { get; init; }
    public string? ConflictType { get; init; }
    public bool Resolved { get; init; }
    public bool Rejecting { get; init; }
}

public sealed class OperationVolume
{
    public string? Id { get; init; }
    public int? Ordinal { get; init; }
    public string EffectiveTimeBegin { get; init; } = "";
    public string EffectiveTimeEnd { get; init; } = "";
    public string? ActualTimeEnd { get; init; }
    public Altitude? MinAltitude { get; init; }
    public Altitude? MaxAltitude { get; init; }
    public Geometry? OperationGeography { get; init; }
    public bool? BeyondVisualLineOfSight { get; init; }
}

public class Ops
{
    public string OperationPlanId { get; init; } = "";
    public string? FlightPlanId { get; init; }
    public string? Operator { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? State { get; init; }
    public string? ClosureReason { get; init; }
    public string? SubmitTime { get; init; }
    public string? UpdateTime { get; init; }
    public List<OperationVolume> OperationVolumes { get; init; } = new();
    public List<OperationVolume> OffNominalVolumes { get; init; } = new();
    public Contact? Contact { get; init; }
    public string? ModeOfOperation { get; init; }
    public double? SwarmSize { get; init; }

    public OpsSchema Schema { get; init; } = OpsSchema.Ed269;

    // ED-318-only fields. Null/empty on an ED-269 plan.
    public string? ProviderId { get; init; }
    public ApprovalStatus? Approval { get; init; }
    public ApprovalStatus? TakeoffClearance { get; init; }
    public List<OpsConflict> Conflicts { get; init; } = new();
}

public sealed class ParsedOps : Ops
{
    public double ComputedArea { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public int ZoneCount { get; init; }
    public string? Color { get; init; }

    /// <summary>Position of this operation in the imported dataset (reassigned if operations are removed).</summary>
    public int Ordinal { get; set; }

    /// <summary>Lon/lat bounds over all volumes, computed once at import for map viewport and report queries.</summary>
    public NetTopologySuite.Geometries.Envelope Bounds { get; init; } = new();

    /// <summary>Lower-cased title, id, operator and description joined by U+0001 (never typed by a user), for substring search.</summary>
    public string SearchText { get; init; } = "";

    public IEnumerable<OperationVolume> AllVolumes => OperationVolumes.Concat(OffNominalVolumes);
}
