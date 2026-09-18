using System.Text.Json;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services;

/// <summary>Parses a raw GeoJSON-shaped {type, coordinates} element into the normalized Geometry model.</summary>
internal static class GeometryParser
{
    public static Geometry? Parse(JsonElement geom)
    {
        if (geom.ValueKind != JsonValueKind.Object) return null;
        var type = JsonHelpers.GetString(geom, "type");
        if (type != "Polygon" && type != "MultiPolygon") return null;
        if (!geom.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array) return null;

        if (type == "Polygon")
        {
            var polygon = ParsePolygonCoords(coords);
            return new Geometry { Type = "Polygon", Polygons = new[] { polygon } };
        }

        var polygons = coords.EnumerateArray().Select(ParsePolygonCoords).ToArray();
        return new Geometry { Type = "MultiPolygon", Polygons = polygons };
    }

    private static double[][][] ParsePolygonCoords(JsonElement polygonCoords) =>
        polygonCoords.EnumerateArray()
            .Select(ring => ring.EnumerateArray()
                .Select(pt => pt.EnumerateArray().Select(n => n.GetDouble()).ToArray())
                .ToArray())
            .ToArray();
}
