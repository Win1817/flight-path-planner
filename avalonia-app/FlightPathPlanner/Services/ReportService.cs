using FlightPathPlanner.Models;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace FlightPathPlanner.Services;

/// <summary>Ported from src/utils/reportUtils.ts — keep in sync with that file's intersects logic.
/// Uses NetTopologySuite's planar Intersects() on raw lon/lat coordinates, matching turf's
/// booleanIntersects (also planar, no geodesic correction) so results agree with the web app.</summary>
public static class ReportService
{
    private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory();

    private static Coordinate[] ToCoordinates(double[][] ring) =>
        ring.Select(pt => new Coordinate(pt[0], pt[1])).ToArray();

    private static Polygon ToNtsPolygon(double[][][] rings)
    {
        var shell = Factory.CreateLinearRing(ToCoordinates(rings[0]));
        var holes = rings.Skip(1).Select(r => Factory.CreateLinearRing(ToCoordinates(r))).ToArray();
        return Factory.CreatePolygon(shell, holes);
    }

    private static NtsGeometry? ToNtsGeometry(Models.Geometry? geometry)
    {
        if (geometry == null || geometry.Polygons.Length == 0) return null;
        try
        {
            var polys = geometry.Polygons.Select(ToNtsPolygon).ToArray();
            return polys.Length == 1 ? polys[0] : Factory.CreateMultiPolygon(polys);
        }
        catch
        {
            return null;
        }
    }

    private sealed record OpFeatureCache(ParsedOps Op, List<NtsGeometry> Features);

    private static List<OpFeatureCache> BuildOpFeatureCache(IEnumerable<ParsedOps> ops) =>
        ops.Select(op => new OpFeatureCache(
            op,
            op.AllVolumes
                .Select(v => ToNtsGeometry(v.OperationGeography))
                .Where(g => g != null)
                .Select(g => g!)
                .ToList()))
            .ToList();

    private static bool CachedOpIntersects(OpFeatureCache cached, NtsGeometry aorFeature) =>
        cached.Features.Any(f =>
        {
            try { return f.Intersects(aorFeature); }
            catch { return false; }
        });

    /// <summary>The ops whose operation volumes geographically intersect the given AoR.</summary>
    public static List<ParsedOps> GetOpsInAor(IReadOnlyList<ParsedOps> ops, ParsedAor aor)
    {
        var aorFeature = ToNtsGeometry(aor.Geometry);
        if (aorFeature == null) return new List<ParsedOps>();
        var cache = BuildOpFeatureCache(ops);
        return cache.Where(c => CachedOpIntersects(c, aorFeature)).Select(c => c.Op).ToList();
    }

    public sealed record AorReportRow(ParsedAor Aor, int MatchCount);

    /// <summary>For every AoR, how many of the given ops geographically intersect it.</summary>
    public static List<AorReportRow> GetAorReportSummary(IReadOnlyList<ParsedAor> aors, IReadOnlyList<ParsedOps> ops)
    {
        var opCache = BuildOpFeatureCache(ops);
        return aors.Select(aor =>
        {
            var aorFeature = ToNtsGeometry(aor.Geometry);
            if (aorFeature == null) return new AorReportRow(aor, 0);
            return new AorReportRow(aor, opCache.Count(c => CachedOpIntersects(c, aorFeature)));
        }).ToList();
    }

    /// <summary>The ops whose operation volumes fall within radiusKm of the given center point.</summary>
    public static List<ParsedOps> GetOpsNearPoint(IReadOnlyList<ParsedOps> ops, double centerLon, double centerLat, double radiusKm)
    {
        var circle = ToNtsPolygon(GeoMath.Circle(centerLon, centerLat, radiusKm));
        var cache = BuildOpFeatureCache(ops);
        return cache.Where(c => CachedOpIntersects(c, circle)).Select(c => c.Op).ToList();
    }
}
