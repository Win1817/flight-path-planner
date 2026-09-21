using System.Buffers;
using System.Text;
using System.Text.Json;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services;

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
        int chunkFeatures = DefaultChunkFeatures, CancellationToken ct = default)
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
            WriteGeometry(w, circle);
            writer.EndFeature();
        }

        int count = 0;
        foreach (var aor in aors)
        {
            if ((++count & 255) == 0) ct.ThrowIfCancellationRequested();
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
            WriteGeometry(w, aor.Geometry);
            writer.EndFeature();
        }

        foreach (var op in ops)
        {
            if ((++count & 255) == 0) ct.ThrowIfCancellationRequested();
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
                WriteGeometry(w, geography);
                writer.EndFeature();
                index++;
            }
        }

        writer.Finish();
        return chunks;
    }

    // 1e-6 degrees is ~0.1 m: plenty for display, and it trims the payload by a third.
    private static double Q(double v) => Math.Round(v, 6);

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
                w.WriteNumberValue(Q(point[0]));
                w.WriteNumberValue(Q(point[1]));
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();
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
