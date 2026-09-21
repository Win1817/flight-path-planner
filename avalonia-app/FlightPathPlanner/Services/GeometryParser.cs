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

    /// <summary>Rings -> points -> numbers. Written with sized arrays and plain loops: this is the hottest loop of a large import
    /// (millions of vertices), and the LINQ version allocated an iterator, a list and an array for every point.</summary>
    public static double[][][] ParsePolygonCoords(JsonElement polygonCoords)
    {
        var rings = new double[polygonCoords.GetArrayLength()][][];
        int r = 0;
        foreach (var ring in polygonCoords.EnumerateArray())
        {
            var points = new double[ring.GetArrayLength()][];
            int p = 0;
            foreach (var point in ring.EnumerateArray())
            {
                var values = new double[point.GetArrayLength()];
                int v = 0;
                foreach (var number in point.EnumerateArray()) values[v++] = number.GetDouble();
                points[p++] = values;
            }
            rings[r++] = points;
        }
        return rings;
    }
}
