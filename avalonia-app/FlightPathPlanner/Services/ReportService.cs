using System.Runtime.CompilerServices;
using FlightPathPlanner.Models;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace FlightPathPlanner.Services;

/// <summary>Ported from src/utils/reportUtils.ts — same planar intersection semantics as turf's booleanIntersects (NetTopologySuite
/// <c>Intersects</c> on raw lon/lat). Large datasets are handled by (1) rejecting non-overlapping candidates with the bounds computed
/// at import before any exact test, (2) building each operation's NTS geometry at most once (cached for the operation's lifetime), and
/// (3) running the all-AoR summary in parallel.</summary>
public static class ReportService
{
    private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory();

    // Exact geometry per operation, built on first need. Weak so removed operations don't pin memory.
    private static readonly ConditionalWeakTable<ParsedOps, NtsGeometry[]> OpGeometryCache = new();

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
            return null; // unusable geometry: treated as "intersects nothing", as before
        }
    }

    private static NtsGeometry[] GeometriesOf(ParsedOps op) =>
        OpGeometryCache.GetValue(op, o => o.AllVolumes
            .Select(v => ToNtsGeometry(v.OperationGeography))
            .Where(g => g != null)
            .Select(g => g!)
            .ToArray());

    private static bool OpIntersects(ParsedOps op, NtsGeometry target)
    {
        if (!op.Bounds.Intersects(target.EnvelopeInternal)) return false;
        foreach (var feature in GeometriesOf(op))
        {
            try { if (feature.Intersects(target)) return true; }
            catch { /* invalid geometry: no match, as before */ }
        }
        return false;
    }

    /// <summary>The ops whose operation volumes geographically intersect the given AoR.</summary>
    public static List<ParsedOps> GetOpsInAor(IReadOnlyList<ParsedOps> ops, ParsedAor aor)
    {
        var aorFeature = ToNtsGeometry(aor.Geometry);
        if (aorFeature == null) return new List<ParsedOps>();
        return ops.Where(op => OpIntersects(op, aorFeature)).ToList();
    }

    public sealed record AorReportRow(ParsedAor Aor, int MatchCount);

    /// <summary>For every AoR, how many of the given ops geographically intersect it. Safe to run on a worker thread.</summary>
    public static List<AorReportRow> GetAorReportSummary(IReadOnlyList<ParsedAor> aors, IReadOnlyList<ParsedOps> ops, CancellationToken ct = default)
    {
        var rows = new AorReportRow[aors.Count];
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

        Parallel.For(0, aors.Count, options, i =>
        {
            var aor = aors[i];
            var feature = ToNtsGeometry(aor.Geometry);
            int matches = 0;
            if (feature != null)
            {
                foreach (var op in ops)
                {
                    if (OpIntersects(op, feature)) matches++;
                }
            }
            rows[i] = new AorReportRow(aor, matches);
        });
        return rows.ToList();
    }

    /// <summary>The ops whose operation volumes fall within radiusKm of the given center point.</summary>
    public static List<ParsedOps> GetOpsNearPoint(IReadOnlyList<ParsedOps> ops, double centerLon, double centerLat, double radiusKm)
    {
        var circle = ToNtsPolygon(GeoMath.Circle(centerLon, centerLat, radiusKm));
        return ops.Where(op => OpIntersects(op, circle)).ToList();
    }
}
