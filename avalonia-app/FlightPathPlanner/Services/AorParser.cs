using System.Text.Json;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services;

/// <summary>Ported from src/utils/aorUtils.ts — keep in sync with that file's parsing rules.</summary>
public static class AorParser
{
    public static readonly string[] AorColors =
    {
        "#FFD700", "#FB923C", "#F87171", "#C084FC", "#38BDF8",
        "#4ADE80", "#F472B6", "#A78BFA", "#FCD34D", "#2DD4BF",
    };

    // Legacy "responsibility area" schema: geometry is a single object, limits are top-level.
    private static bool IsLegacyAor(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object
        && raw.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
        && raw.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
        && raw.TryGetProperty("designator", out var des) && des.ValueKind == JsonValueKind.String
        && raw.TryGetProperty("geometry", out var geom) && geom.ValueKind == JsonValueKind.Object
        && raw.TryGetProperty("lowerLimit", out var ll) && ll.ValueKind == JsonValueKind.Number
        && raw.TryGetProperty("upperLimit", out var ul) && ul.ValueKind == JsonValueKind.Number;

    // Real-world "airspace zone" schema: geometry is an array of {horizontalProjection, lowerLimit, upperLimit, ...}.
    private static bool IsZoneAor(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object) return false;
        bool hasName = raw.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String;
        bool hasIdentifier = raw.TryGetProperty("identifier", out var i) && i.ValueKind == JsonValueKind.String;
        bool hasGeomArray = raw.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Array && g.GetArrayLength() > 0;
        return (hasName || hasIdentifier) && hasGeomArray;
    }

    private static Aor NormalizeLegacyAor(JsonElement raw)
    {
        var geometry = GeometryParser.Parse(raw.GetProperty("geometry"))
            ?? throw new InvalidDataException("Legacy AoR has unusable geometry");

        return new Aor
        {
            Id = raw.GetProperty("id").GetString()!,
            Name = raw.GetProperty("name").GetString()!,
            Designator = raw.GetProperty("designator").GetString()!,
            Geometry = geometry,
            LowerLimit = raw.GetProperty("lowerLimit").GetDouble(),
            UpperLimit = raw.GetProperty("upperLimit").GetDouble(),
            VerticalLimitsUom = JsonHelpers.GetString(raw, "verticalLimitsUom") ?? "",
            VerticalReferenceType = JsonHelpers.GetString(raw, "verticalReferenceType") ?? "",
            AutoReject = JsonHelpers.GetBool(raw, "autoReject"),
            AutoApprovalEnabled = JsonHelpers.GetBool(raw, "autoApprovalEnabled"),
            AorEnabled = JsonHelpers.GetBool(raw, "aorEnabled"),
            AutoTakeOffClearanceEnabled = JsonHelpers.GetBool(raw, "autoTakeOffClearanceEnabled"),
            MaxSimultaneousOperationsEnabled = JsonHelpers.GetBool(raw, "maxSimultaneousOperationsEnabled"),
            MaxSimultaneousOperations = JsonHelpers.GetNumber(raw, "maxSimultaneousOperations"),
            FeatureType = JsonHelpers.GetString(raw, "featureType"),
        };
    }

    private static List<double[][][]> ProjectionToPolygons(JsonElement projection)
    {
        var type = JsonHelpers.GetString(projection, "type");
        var polygons = new List<double[][][]>();

        if (type == "Circle")
        {
            var radius = JsonHelpers.GetNumber(projection, "radius"); // meters
            if (projection.TryGetProperty("center", out var centerEl) &&
                centerEl.ValueKind == JsonValueKind.Array && centerEl.GetArrayLength() >= 2 &&
                radius.HasValue)
            {
                var center = centerEl.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var ring = GeoMath.Circle(center[0], center[1], radiusKm: radius.Value / 1000.0);
                polygons.Add(ring);
            }
        }
        else if (type == "Polygon" && projection.TryGetProperty("coordinates", out var polyCoords))
        {
            polygons.Add(ParsePolygonRings(polyCoords));
        }
        else if (type == "MultiPolygon" && projection.TryGetProperty("coordinates", out var multiCoords))
        {
            polygons.AddRange(multiCoords.EnumerateArray().Select(ParsePolygonRings));
        }

        return polygons;
    }

    private static double[][][] ParsePolygonRings(JsonElement polygonCoords) =>
        polygonCoords.EnumerateArray()
            .Select(ring => ring.EnumerateArray()
                .Select(pt => pt.EnumerateArray().Select(n => n.GetDouble()).ToArray())
                .ToArray())
            .ToArray();

    private static Aor NormalizeZoneAor(JsonElement raw)
    {
        var geometryEntries = raw.GetProperty("geometry");

        var polygons = new List<double[][][]>();
        double lowerLimit = double.PositiveInfinity;
        double upperLimit = double.NegativeInfinity;
        string verticalLimitsUom = "FT";
        string verticalReferenceType = "";

        foreach (var entry in geometryEntries.EnumerateArray())
        {
            var projection = JsonHelpers.GetObject(entry, "horizontalProjection");
            if (projection.HasValue)
            {
                polygons.AddRange(ProjectionToPolygons(projection.Value));
            }

            var entryLower = JsonHelpers.GetNumber(entry, "lowerLimit");
            var entryUpper = JsonHelpers.GetNumber(entry, "upperLimit");
            if (entryLower.HasValue) lowerLimit = Math.Min(lowerLimit, entryLower.Value);
            if (entryUpper.HasValue) upperLimit = Math.Max(upperLimit, entryUpper.Value);

            var uom = JsonHelpers.GetString(entry, "uomDimensions");
            if (uom != null) verticalLimitsUom = uom;

            var lowerRef = JsonHelpers.GetString(entry, "lowerVerticalReference");
            var upperRef = JsonHelpers.GetString(entry, "upperVerticalReference");
            if (lowerRef != null) verticalReferenceType = lowerRef;
            else if (upperRef != null) verticalReferenceType = upperRef;
        }

        if (polygons.Count == 0)
            throw new InvalidDataException("AoR zone has no usable geometry");

        var geometry = polygons.Count == 1
            ? new Geometry { Type = "Polygon", Polygons = new[] { polygons[0] } }
            : new Geometry { Type = "MultiPolygon", Polygons = polygons.ToArray() };

        JsonElement? applicability = null;
        if (JsonHelpers.GetArray(raw, "applicability") is { } appArr && appArr.GetArrayLength() > 0)
        {
            applicability = appArr[0];
        }

        var id = JsonHelpers.GetString(raw, "zoneId", "identifier", "name") ?? "";
        var name = JsonHelpers.GetString(raw, "name", "identifier") ?? id;
        var designator = JsonHelpers.GetString(raw, "identifier", "name") ?? id;

        return new Aor
        {
            Id = id,
            Name = name,
            Designator = designator,
            Geometry = geometry,
            LowerLimit = double.IsFinite(lowerLimit) ? lowerLimit : 0,
            UpperLimit = double.IsFinite(upperLimit) ? upperLimit : 0,
            VerticalLimitsUom = verticalLimitsUom,
            VerticalReferenceType = verticalReferenceType,
            FeatureType = JsonHelpers.GetString(raw, "type"),
            Restriction = JsonHelpers.GetString(raw, "restriction"),
            Reasons = JsonHelpers.GetStringArray(raw, "reason"),
            Message = JsonHelpers.GetString(raw, "message"),
            EffectiveTimeBegin = applicability.HasValue ? OpsParser.EnsureUtc(JsonHelpers.GetString(applicability.Value, "startDateTime")) : null,
            EffectiveTimeEnd = applicability.HasValue ? OpsParser.EnsureUtc(JsonHelpers.GetString(applicability.Value, "endDateTime")) : null,
        };
    }

    /// <summary>Normalizes a single AoR entry, supporting both known schemas. Returns null if unrecognized/unparseable.</summary>
    private static Aor? NormalizeAor(JsonElement raw)
    {
        try
        {
            if (IsLegacyAor(raw)) return NormalizeLegacyAor(raw);
            if (IsZoneAor(raw)) return NormalizeZoneAor(raw);
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static ParseAorsResult ParseAors(JsonElement data)
    {
        var rawAors = new List<JsonElement>();

        if (data.ValueKind == JsonValueKind.Array)
        {
            rawAors.AddRange(data.EnumerateArray());
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            if (IsLegacyAor(data) || IsZoneAor(data))
            {
                rawAors.Add(data);
            }
            else if (JsonHelpers.GetArray(data, "aors") is { } aorsArr) rawAors.AddRange(aorsArr.EnumerateArray());
            else if (JsonHelpers.GetArray(data, "responsibility_areas") is { } ra) rawAors.AddRange(ra.EnumerateArray());
            else if (JsonHelpers.GetArray(data, "zones") is { } zones) rawAors.AddRange(zones.EnumerateArray());
        }

        var normalized = rawAors.Select(NormalizeAor).ToList();
        var aors = normalized.Where(a => a != null).Select(a => a!).ToList();

        if (aors.Count == 0)
            throw new InvalidDataException("No valid AoR data found in the file");

        return new ParseAorsResult { Aors = aors, Skipped = normalized.Count - aors.Count };
    }

    public static ParsedAor ProcessAor(Aor aor, int index = 0) => new()
    {
        Id = aor.Id,
        Name = aor.Name,
        Designator = aor.Designator,
        Geometry = aor.Geometry,
        LowerLimit = aor.LowerLimit,
        UpperLimit = aor.UpperLimit,
        VerticalLimitsUom = aor.VerticalLimitsUom,
        VerticalReferenceType = aor.VerticalReferenceType,
        AutoReject = aor.AutoReject,
        AutoApprovalEnabled = aor.AutoApprovalEnabled,
        AorEnabled = aor.AorEnabled,
        AutoTakeOffClearanceEnabled = aor.AutoTakeOffClearanceEnabled,
        MaxSimultaneousOperationsEnabled = aor.MaxSimultaneousOperationsEnabled,
        MaxSimultaneousOperations = aor.MaxSimultaneousOperations,
        FeatureType = aor.FeatureType,
        Restriction = aor.Restriction,
        Reasons = aor.Reasons,
        Message = aor.Message,
        EffectiveTimeBegin = aor.EffectiveTimeBegin,
        EffectiveTimeEnd = aor.EffectiveTimeEnd,
        ComputedArea = aor.Geometry.ComputeArea(),
        Color = AorColors[index % AorColors.Length],
    };
}
