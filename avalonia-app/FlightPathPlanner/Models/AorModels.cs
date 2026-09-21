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

    /// <summary>Position of this AoR in the imported dataset. Stable for the dataset's lifetime; ids may repeat, this never does.</summary>
    public int Ordinal { get; set; }

    /// <summary>Lon/lat bounds, computed once at import so map viewport queries never walk the geometry.</summary>
    public NetTopologySuite.Geometries.Envelope Bounds { get; init; } = new();

    /// <summary>Lower-cased text of every searchable field, joined by U+0001 (which a user cannot type, so a query never matches
    /// across two fields). Built once at import so each search is a plain substring scan.</summary>
    public string SearchText { get; init; } = "";
}

public sealed class ParseAorsResult
{
    public required List<Aor> Aors { get; init; }
    public int Skipped { get; init; } // entries that looked like AoRs but failed to normalize
}
