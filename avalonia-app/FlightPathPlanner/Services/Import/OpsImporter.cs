using System.Diagnostics;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services.Import;

/// <summary>An imported OPS dataset: immutable, built off the UI thread.</summary>
public sealed class OpsDataset
{
    public static readonly OpsDataset Empty = new(Array.Empty<ParsedOps>(), new SpatialFeatureIndex(Array.Empty<NetTopologySuite.Geometries.Envelope>(), Array.Empty<double>()),
        Array.Empty<string>(), null, 0, new ImportMetrics());

    public OpsDataset(ParsedOps[] items, SpatialFeatureIndex index, string[] closureReasons, (DateTimeOffset from, DateTimeOffset to)? overallRange, int skipped, ImportMetrics metrics)
    {
        Items = items;
        Index = index;
        ClosureReasons = closureReasons;
        OverallRange = overallRange;
        Skipped = skipped;
        Metrics = metrics;
    }

    public ParsedOps[] Items { get; }
    public SpatialFeatureIndex Index { get; }
    public string[] ClosureReasons { get; }

    /// <summary>Earliest start / latest end across all operations; seeds the timeframe filter.</summary>
    public (DateTimeOffset from, DateTimeOffset to)? OverallRange { get; }
    public int Skipped { get; }
    public ImportMetrics Metrics { get; }
}

public static class OpsImporter
{
    private static readonly string[] WrapperNames = { "plans", "operations", "flight_plans" };

    public static async Task<OpsDataset> ImportAsync(ImportSource source, Action<ImportProgress>? onProgress, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var items = new List<ParsedOps>();
        int records = 0, skipped = 0;
        long polygons = 0, vertices = 0, modelTicks = 0;
        DateTimeOffset? min = null, max = null;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var progress = onProgress == null ? null : new ThrottledProgress(onProgress);

        void Report(long bytes, ImportStage stage = ImportStage.Parsing, bool force = false) =>
            progress?.Report(new ImportProgress(stage, bytes, source.Length, items.Count, skipped, total.Elapsed), force);

        void OnRecord(System.Text.Json.JsonElement element)
        {
            long t0 = Stopwatch.GetTimestamp();
            records++;
            try
            {
                // Anything that isn't an object (a stray string or number in the array) is junk, not an empty operation.
                if (element.ValueKind != System.Text.Json.JsonValueKind.Object) throw new InvalidDataException("Not an operation plan object.");
                var op = OpsParser.ProcessOps(OpsParser.NormalizeOps(element), items.Count);
                items.Add(op);
                if (!string.IsNullOrEmpty(op.ClosureReason)) reasons.Add(op.ClosureReason);
                if (min == null || op.StartTime < min) min = op.StartTime;
                if (max == null || op.EndTime > max) max = op.EndTime;
                foreach (var v in op.AllVolumes)
                {
                    if (v.OperationGeography == null) continue;
                    polygons += v.OperationGeography.Polygons.Length;
                    vertices += v.OperationGeography.VertexCount;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped++; // one malformed plan must not sink the whole import
            }
            modelTicks += Stopwatch.GetTimestamp() - t0;
        }

        Report(0, ImportStage.Reading, force: true);
        var streamed = await RecordStreamer.StreamAsync(
            source, WrapperNames, OpsParser.IsSingleOpsRecord, OnRecord, bytes => Report(bytes), ct).ConfigureAwait(false);

        if (!streamed.Handled || items.Count == 0)
            throw new InvalidDataException("Invalid OPS data format");

        ct.ThrowIfCancellationRequested();
        Report(source.Length, ImportStage.Indexing, force: true);
        var indexTimer = Stopwatch.StartNew();
        var array = items.ToArray();
        var index = new SpatialFeatureIndex(array.Select(o => o.Bounds).ToArray(), array.Select(o => o.ComputedArea).ToArray());
        indexTimer.Stop();
        progress?.Flush();

        var metrics = new ImportMetrics
        {
            Description = "OPS import",
            FileSizeBytes = source.Length,
            Records = records,
            Valid = array.Length,
            Skipped = skipped,
            Polygons = polygons,
            Vertices = vertices,
            FileRead = streamed.ReadTime,
            ModelAndGeometry = TimeSpan.FromSeconds((double)modelTicks / Stopwatch.Frequency),
            SpatialIndex = indexTimer.Elapsed,
            Total = total.Elapsed,
            PeakWorkingSetBytes = ImportMetrics.CurrentPeakWorkingSet(),
            ManagedBytesAtEnd = GC.GetTotalMemory(false),
        };
        metrics.JsonParse = metrics.Total - metrics.FileRead - metrics.ModelAndGeometry - metrics.SpatialIndex;
        if (metrics.JsonParse < TimeSpan.Zero) metrics.JsonParse = TimeSpan.Zero;

        return new OpsDataset(array, index, reasons.ToArray(), min is { } a && max is { } b ? (a, b) : null, skipped, metrics);
    }
}
