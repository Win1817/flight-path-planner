using FlightPathPlanner.Services;
using Envelope = NetTopologySuite.Geometries.Envelope;

namespace FlightPathPlanner.Models;

/// <summary>
/// Normalized geometry: always stored as a list of polygons (each a list of rings, each a list
/// of [lon, lat] points), regardless of whether the source was GeoJSON Polygon or MultiPolygon.
/// A Polygon is represented as a single-entry list — this lets area/intersects code treat both
/// uniformly instead of replicating TypeScript's Polygon-vs-MultiPolygon union type.
/// </summary>
public sealed class Geometry
{
    public required string Type { get; init; } // "Polygon" or "MultiPolygon" (for GeoJSON round-tripping)
    public required double[][][][] Polygons { get; init; }

    public double ComputeArea() => GeoMath.MultiPolygonArea(Polygons);

    /// <summary>Lon/lat bounding box of every vertex, or a null envelope when the geometry has no points.</summary>
    public Envelope ComputeBounds()
    {
        var env = new Envelope();
        foreach (var polygon in Polygons)
            foreach (var ring in polygon)
                foreach (var point in ring)
                    env.ExpandToInclude(point[0], point[1]);
        return env;
    }

    public int VertexCount
    {
        get
        {
            int count = 0;
            foreach (var polygon in Polygons)
                foreach (var ring in polygon) count += ring.Length;
            return count;
        }
    }
}
