namespace FlightPathPlanner.Models;

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
