namespace FlightPathPlanner.Models;

public class Aor
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Designator { get; init; } = "";
    public required Geometry Geometry { get; init; }
    public double LowerLimit { get; init; }
    public double UpperLimit { get; init; }
    public string VerticalLimitsUom { get; init; } = "";
    public string VerticalReferenceType { get; init; } = "";

    // UTM-workflow fields, present only on the "responsibility area" (legacy) schema.
    public bool? AutoReject { get; init; }
    public bool? AutoApprovalEnabled { get; init; }
    public bool? AorEnabled { get; init; }
    public bool? AutoTakeOffClearanceEnabled { get; init; }
    public bool? MaxSimultaneousOperationsEnabled { get; init; }
    public double? MaxSimultaneousOperations { get; init; }
    public string? FeatureType { get; init; }

    // Zone/NOTAM fields, present only on the real-world "airspace zone" schema.
    public string? Restriction { get; init; }
    public List<string>? Reasons { get; init; }
    public string? Message { get; init; }
    public string? EffectiveTimeBegin { get; init; }
    public string? EffectiveTimeEnd { get; init; }
}

public sealed class ParsedAor : Aor
{
    public double ComputedArea { get; init; }
    public string? Color { get; init; }
}

public sealed class ParseAorsResult
{
    public required List<Aor> Aors { get; init; }
    public int Skipped { get; init; } // entries that looked like AoRs but failed to normalize
}
