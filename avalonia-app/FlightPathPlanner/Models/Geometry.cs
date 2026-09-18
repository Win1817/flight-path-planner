using FlightPathPlanner.Services;

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
}
