using System.Buffers;
using System.Text;
using System.Text.Json;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services;

/// <summary>How much detail a map update may carry. A zoomed-out view of a big dataset cannot show sub-pixel vertices, and a file
/// that reaches hundreds of MB usually does so through vertex-heavy shapes (a few thousand zones of thousands of vertices each), so
/// counting features alone does not bound the payload: a 7,000-zone / 147 MB file produced a 52 MB first render. Rings are
/// therefore simplified to <see cref="Tolerance"/> (about one screen pixel) and feature output stops once
/// <see cref="VertexBudget"/> vertices have been written. The counters report what was actually written.</summary>
public sealed class MapDetail
{
    /// <summary>Douglas-Peucker tolerance in degrees; 0 keeps full detail.</summary>
    public double Tolerance { get; init; }

    public int VertexBudget { get; init; } = int.MaxValue;
    public int ItemsWritten { get; internal set; }
    public int VerticesWritten { get; internal set; }
    public bool Truncated { get; internal set; }

    internal bool BudgetSpent => VerticesWritten >= VertexBudget;
}

/// <summary>Builds the GeoJSON features consumed by Assets/map/map.js (property names must match what the page reads).
/// Features are produced in chunks — each chunk is a JSON array of features — so a large selection is streamed to the web view
/// as several modest messages instead of one giant string, and nothing ever builds a FeatureCollection for a whole dataset.</summary>
public static class MapGeoJson
{
    public const string LookupRadiusId = "__lookup_radius__";
    public const int DefaultChunkFeatures = 400;

    /// <summary>Whole-collection form (small inputs and tests): {"type":"FeatureCollection","features":[...]}.</summary>
    public static string Build(IEnumerable<ParsedOps> ops, IEnumerable<ParsedAor> aors,
        (double lon, double lat, double radiusKm)? lookupRadius = null, IReadOnlySet<string>? highlightIds = null)
    {
        var chunks = BuildChunks(ops, aors, lookupRadius, highlightIds, chunkFeatures: int.MaxValue);
        var body = chunks.Count == 0 ? "[]" : chunks[0];
        return "{\"type\":\"FeatureCollection\",\"features\":" + body + "}";
    }

    /// <param name="highlightIds">Ids (opsId / aorId) to flag with "hl":1 so the page can draw them emphasised without a large id list.</param>
    /// <returns>Chunks, each a JSON array of features. Empty input yields no chunks.</returns>
    public static List<string> BuildChunks(IEnumerable<ParsedOps> ops, IEnumerable<ParsedAor> aors,
        (double lon, double lat, double radiusKm)? lookupRadius = null, IReadOnlySet<string>? highlightIds = null,
        int chunkFeatures = DefaultChunkFeatures, CancellationToken ct = default, MapDetail? detail = null)
    {
        var chunks = new List<string>();
        using var writer = new ChunkWriter(chunks, chunkFeatures);

        if (lookupRadius is { } r)
        {
            var circle = new Models.Geometry { Type = "Polygon", Polygons = new[] { GeoMath.Circle(r.lon, r.lat, r.radiusKm) } };
            writer.BeginFeature();
            var w = writer.Json;
            w.WriteStartObject("properties");
            w.WriteString("dataType", "aor");
            w.WriteString("aorId", LookupRadiusId);
            w.WriteString("name", "Search Radius");
            w.WriteString("designator", $"{r.radiusKm:0.##} km");
            w.WriteNumber("area", circle.ComputeArea());
            w.WriteString("color", "#FB7185"); // Luna danger: the lookup radius must not resemble any OPS/AoR colour
            w.WriteEndObject();
            WriteGeometry(w, circle, null);
            writer.EndFeature();
        }

        int count = 0;
        foreach (var aor in aors)
        {
            if ((++count & 255) == 0) ct.ThrowIfCancellationRequested();
            if (detail is { BudgetSpent: true }) { detail.Truncated = true; break; }
            writer.BeginFeature();
            var w = writer.Json;
            w.WriteStartObject("properties");
            w.WriteString("dataType", "aor");
            w.WriteString("aorId", aor.Id);
            if (highlightIds?.Contains(aor.Id) == true) w.WriteNumber("hl", 1);
            w.WriteString("name", aor.Name);
            w.WriteString("designator", aor.Designator);
            w.WriteNumber("lowerLimit", aor.LowerLimit);
            w.WriteNumber("upperLimit", aor.UpperLimit);
            w.WriteString("limitUnit", aor.VerticalLimitsUom);
            w.WriteString("verticalReference", aor.VerticalReferenceType);
            w.WriteNumber("area", aor.ComputedArea);
            w.WriteString("color", aor.Color ?? "#FFD700");
            w.WriteEndObject();
            WriteGeometry(w, aor.Geometry, detail);
            writer.EndFeature();
            if (detail != null) detail.ItemsWritten++;
        }

        foreach (var op in ops)
        {
            if ((++count & 255) == 0) ct.ThrowIfCancellationRequested();
            if (detail is { BudgetSpent: true }) { detail.Truncated = true; break; }
            int index = 0;
            bool highlighted = highlightIds?.Contains(op.OperationPlanId) == true;
            foreach (var volume in op.AllVolumes)
            {
                var geography = volume.OperationGeography;
                if (geography == null) { index++; continue; }

                writer.BeginFeature();
                var w = writer.Json;
                w.WriteStartObject("properties");
                w.WriteString("opsId", op.OperationPlanId);
                w.WriteString("operationPlanId", op.OperationPlanId);
                w.WriteString("dataType", "ops");
                if (highlighted) w.WriteNumber("hl", 1);
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
                WriteGeometry(w, geography, detail);
                writer.EndFeature();
                index++;
            }
            if (detail != null) detail.ItemsWritten++;
        }

        writer.Finish();
        return chunks;
    }

    // 1e-6 degrees is ~0.1 m: plenty for display, and it trims the payload by a third.
    private static double Q(double v) => Math.Round(v, 6);

    private static void WriteGeometry(Utf8JsonWriter w, Models.Geometry geometry, MapDetail? detail)
    {
        bool single = geometry.Type == "Polygon" && geometry.Polygons.Length == 1;
        w.WriteStartObject("geometry");
        w.WriteString("type", single ? "Polygon" : "MultiPolygon");
        w.WritePropertyName("coordinates");
        if (single) WritePolygon(w, geometry.Polygons[0], detail);
        else
        {
            w.WriteStartArray();
            foreach (var polygon in geometry.Polygons) WritePolygon(w, polygon, detail);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static void WritePolygon(Utf8JsonWriter w, double[][][] rings, MapDetail? detail)
    {
        double tolerance = detail?.Tolerance ?? 0;
        w.WriteStartArray();
        for (int r = 0; r < rings.Length; r++)
        {
            var ring = rings[r];
            // A hole smaller than a pixel is invisible; the outer ring is always kept so the shape still exists.
            if (r > 0 && tolerance > 0 && IsSubPixel(ring, tolerance)) continue;

            var points = tolerance > 0 ? SimplifyRing(ring, tolerance) : ring;
            if (detail != null) detail.VerticesWritten += points.Length;
            w.WriteStartArray();
            foreach (var point in points)
            {
                w.WriteStartArray();
                w.WriteNumberValue(Q(point[0]));
                w.WriteNumberValue(Q(point[1]));
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }

    private static bool IsSubPixel(double[][] ring, double tolerance)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in ring)
        {
            if (p[0] < minX) minX = p[0];
            if (p[0] > maxX) maxX = p[0];
            if (p[1] < minY) minY = p[1];
            if (p[1] > maxY) maxY = p[1];
        }
        return maxX - minX < tolerance && maxY - minY < tolerance;
    }

    /// <summary>Douglas-Peucker on a closed ring (planar degrees — this is display-only). The ring is split at the vertex farthest
    /// from its start, since the start and end coincide and a closed ring has no natural baseline. Always returns a closed ring of
    /// at least 4 positions, so the result is still a valid polygon however small the shape is at this zoom.</summary>
    public static double[][] SimplifyRing(double[][] ring, double tolerance)
    {
        int n = ring.Length;
        if (n <= 8) return ring;

        int far = 1;
        double farDist = -1;
        for (int i = 1; i < n - 1; i++)
        {
            double d = Sq(ring[i], ring[0]);
            if (d > farDist) { farDist = d; far = i; }
        }

        double tol2 = tolerance * tolerance;
        if (farDist < tol2) return new[] { ring[0], ring[far], ring[(far + n - 1) / 2], ring[0] }; // the whole ring is under a pixel

        var keep = new bool[n];
        keep[0] = keep[far] = keep[n - 1] = true;
        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, far));
        stack.Push((far, n - 1));
        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            if (e - s < 2) continue;
            int split = -1;
            double worst = tol2;
            for (int i = s + 1; i < e; i++)
            {
                double d = SegmentDistSq(ring[i], ring[s], ring[e]);
                if (d > worst) { worst = d; split = i; }
            }
            if (split < 0) continue;
            keep[split] = true;
            stack.Push((s, split));
            stack.Push((split, e));
        }

        int kept = 0;
        for (int i = 0; i < n; i++) if (keep[i]) kept++;
        if (kept < 4) return new[] { ring[0], ring[far], ring[(far + n - 1) / 2], ring[0] };

        var result = new double[kept][];
        int k = 0;
        for (int i = 0; i < n; i++) if (keep[i]) result[k++] = ring[i];
        return result;
    }

    private static double Sq(double[] a, double[] b)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1];
        return dx * dx + dy * dy;
    }

    private static double SegmentDistSq(double[] p, double[] a, double[] b)
    {
        double dx = b[0] - a[0], dy = b[1] - a[1];
        double len2 = dx * dx + dy * dy;
        if (len2 == 0) return Sq(p, a);
        double t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len2;
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        double px = a[0] + t * dx - p[0], py = a[1] + t * dy - p[1];
        return px * px + py * py;
    }

    /// <summary>Writes features into successive chunks of at most N features each.</summary>
    private sealed class ChunkWriter : IDisposable
    {
        private readonly List<string> _chunks;
        private readonly int _perChunk;
        private readonly ArrayBufferWriter<byte> _buffer = new(1 << 16);
        private readonly Utf8JsonWriter _writer;
        private int _inChunk;
        private bool _open;

        public ChunkWriter(List<string> chunks, int perChunk)
        {
            _chunks = chunks;
            _perChunk = perChunk;
            _writer = new Utf8JsonWriter(_buffer);
        }

        public Utf8JsonWriter Json => _writer;

        public void BeginFeature()
        {
            if (!_open)
            {
                _buffer.Clear();
                _writer.Reset(_buffer);
                _writer.WriteStartArray();
                _open = true;
                _inChunk = 0;
            }
            _writer.WriteStartObject();
            _writer.WriteString("type", "Feature");
        }

        public void EndFeature()
        {
            _writer.WriteEndObject();
            if (++_inChunk >= _perChunk) Close();
        }

        public void Finish()
        {
            if (_open) Close();
        }

        private void Close()
        {
            _writer.WriteEndArray();
            _writer.Flush();
            _chunks.Add(Encoding.UTF8.GetString(_buffer.WrittenSpan));
            _open = false;
        }

        public void Dispose() => _writer.Dispose();
    }
}
