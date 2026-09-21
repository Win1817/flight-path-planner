using System.Text;
using System.Text.Json;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services;

/// <summary>Builds the viewer FeatureCollection consumed by Assets/map/map.js. Ported from opsToGeoJSON /
/// aorsToGeoJSON in the former web app (property names must match what map.js reads).</summary>
public static class MapGeoJson
{
    public const string LookupRadiusId = "__lookup_radius__";

    public static string Build(IEnumerable<ParsedOps> ops, IEnumerable<ParsedAor> aors, (double lon, double lat, double radiusKm)? lookupRadius = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WriteStartArray("features");

            if (lookupRadius is { } r)
            {
                var circle = new Models.Geometry { Type = "Polygon", Polygons = new[] { GeoMath.Circle(r.lon, r.lat, r.radiusKm) } };
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("properties");
                w.WriteString("dataType", "aor");
                w.WriteString("aorId", LookupRadiusId);
                w.WriteString("name", "Search Radius");
                w.WriteString("designator", $"{r.radiusKm:0.##} km");
                w.WriteNumber("area", circle.ComputeArea());
                w.WriteString("color", "#FB7185"); // Luna danger: the lookup radius must not resemble any OPS/AoR colour
                w.WriteEndObject();
                WriteGeometry(w, circle);
                w.WriteEndObject();
            }

            foreach (var aor in aors)
            {
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("properties");
                w.WriteString("dataType", "aor");
                w.WriteString("aorId", aor.Id);
                w.WriteString("name", aor.Name);
                w.WriteString("designator", aor.Designator);
                w.WriteNumber("lowerLimit", aor.LowerLimit);
                w.WriteNumber("upperLimit", aor.UpperLimit);
                w.WriteString("limitUnit", aor.VerticalLimitsUom);
                w.WriteString("verticalReference", aor.VerticalReferenceType);
                w.WriteNumber("area", aor.ComputedArea);
                w.WriteString("color", aor.Color ?? "#FFD700");
                w.WriteEndObject();
                WriteGeometry(w, aor.Geometry);
                w.WriteEndObject();
            }

            foreach (var op in ops)
            {
                int index = 0;
                foreach (var volume in op.AllVolumes)
                {
                    var geography = volume.OperationGeography;
                    if (geography == null) { index++; continue; }

                    w.WriteStartObject();
                    w.WriteString("type", "Feature");
                    w.WriteStartObject("properties");
                    w.WriteString("opsId", op.OperationPlanId);
                    w.WriteString("operationPlanId", op.OperationPlanId);
                    w.WriteString("dataType", "ops");
                    w.WriteString("operator", op.Operator);
                    w.WriteString("title", string.IsNullOrEmpty(op.Title) ? "Untitled Operation" : op.Title);
                    w.WriteString("description", op.Description ?? "");
                    w.WriteString("state", op.State);
                    w.WriteString("closureReason", op.ClosureReason);
                    w.WriteNumber("volumeIndex", index);
                    if (volume.MinAltitude != null) w.WriteNumber("minAltitude", volume.MinAltitude.AltitudeValue);
                    if (volume.MaxAltitude != null) w.WriteNumber("maxAltitude", volume.MaxAltitude.AltitudeValue);
                    w.WriteString("altitudeUnit", volume.MaxAltitude?.UnitsOfMeasure ?? volume.MinAltitude?.UnitsOfMeasure ?? "FT");
                    w.WriteString("startTime", volume.EffectiveTimeBegin);
                    w.WriteString("endTime", volume.EffectiveTimeEnd);
                    w.WriteNumber("area", geography.ComputeArea());
                    w.WriteString("color", op.Color);
                    w.WriteEndObject();
                    WriteGeometry(w, geography);
                    w.WriteEndObject();
                    index++;
                }
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteGeometry(Utf8JsonWriter w, Models.Geometry geometry)
    {
        bool single = geometry.Type == "Polygon" && geometry.Polygons.Length == 1;
        w.WriteStartObject("geometry");
        w.WriteString("type", single ? "Polygon" : "MultiPolygon");
        w.WritePropertyName("coordinates");
        if (single) WritePolygon(w, geometry.Polygons[0]);
        else
        {
            w.WriteStartArray();
            foreach (var polygon in geometry.Polygons) WritePolygon(w, polygon);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static void WritePolygon(Utf8JsonWriter w, double[][][] rings)
    {
        w.WriteStartArray();
        foreach (var ring in rings)
        {
            w.WriteStartArray();
            foreach (var point in ring)
            {
                w.WriteStartArray();
                w.WriteNumberValue(point[0]);
                w.WriteNumberValue(point[1]);
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }
}
