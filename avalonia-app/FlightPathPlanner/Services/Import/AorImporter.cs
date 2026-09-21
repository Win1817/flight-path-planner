using System.Diagnostics;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services.Import;

/// <summary>An imported AoR dataset: immutable, built off the UI thread, then handed to the view model in one step.</summary>
public sealed class AorDataset
{
    public static readonly AorDataset Empty = new(Array.Empty<ParsedAor>(), new SpatialFeatureIndex(Array.Empty<NetTopologySuite.Geometries.Envelope>(), Array.Empty<double>()), 0, new ImportMetrics());

    public AorDataset(ParsedAor[] items, SpatialFeatureIndex index, int skipped, ImportMetrics metrics)
    {
        Items = items;
        Index = index;
        Skipped = skipped;
        Metrics = metrics;
    }

    public ParsedAor[] Items { get; }
    public SpatialFeatureIndex Index { get; }
    public int Skipped { get; }
    public ImportMetrics Metrics { get; }
}

public static class AorImporter
{
    private static readonly string[] WrapperNames = { "aors", "responsibility_areas", "zones" };

    /// <summary>Streams the file, builds models and geometry bounds record by record, then the spatial index. Runs entirely on the
    /// calling thread pool thread; nothing here touches UI state. Throws (leaving nothing partial) on cancellation or invalid input.</summary>
    public static async Task<AorDataset> ImportAsync(ImportSource source, Action<ImportProgress>? onProgress, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var items = new List<ParsedAor>();
        int records = 0, skipped = 0;
        long polygons = 0, vertices = 0;
        long modelTicks = 0;
        var progress = onProgress == null ? null : new ThrottledProgress(onProgress);

        void Report(long bytes, ImportStage stage = ImportStage.Parsing, bool force = false) =>
            progress?.Report(new ImportProgress(stage, bytes, source.Length, items.Count, skipped, total.Elapsed), force);

        void OnRecord(System.Text.Json.JsonElement element)
        {
            long t0 = Stopwatch.GetTimestamp();
            records++;
            var aor = AorParser.NormalizeAor(element);
            if (aor == null)
            {
                skipped++;
            }
            else
            {
                var processed = AorParser.ProcessAor(aor, items.Count);
                items.Add(processed);
                polygons += processed.Geometry.Polygons.Length;
                vertices += processed.Geometry.VertexCount;
            }
            modelTicks += Stopwatch.GetTimestamp() - t0;
        }

        Report(0, ImportStage.Reading, force: true);
        var streamed = await RecordStreamer.StreamAsync(
            source, WrapperNames,
            looksLikeSingleRecord: e => AorParser.IsLegacyAor(e) || AorParser.IsZoneAor(e),
            OnRecord,
            bytes => Report(bytes),
            ct).ConfigureAwait(false);

        if (!streamed.Handled || items.Count == 0)
            throw new InvalidDataException("No valid AoR data found in the file");

        ct.ThrowIfCancellationRequested();
        Report(source.Length, ImportStage.Indexing, force: true);
        var indexTimer = Stopwatch.StartNew();
        var array = items.ToArray();
        var index = new SpatialFeatureIndex(array.Select(a => a.Bounds).ToArray(), array.Select(a => a.ComputedArea).ToArray());
        indexTimer.Stop();

        progress?.Flush();
        var modelTime = TimeSpan.FromSeconds((double)modelTicks / Stopwatch.Frequency);
        var metrics = new ImportMetrics
        {
            Description = "AoR import",
            FileSizeBytes = source.Length,
            Records = records,
            Valid = array.Length,
            Skipped = skipped,
            Polygons = polygons,
            Vertices = vertices,
            FileRead = streamed.ReadTime,
            ModelAndGeometry = modelTime,
            JsonParse = TimeSpan.Zero,
            SpatialIndex = indexTimer.Elapsed,
            Total = total.Elapsed,
            PeakWorkingSetBytes = ImportMetrics.CurrentPeakWorkingSet(),
            ManagedBytesAtEnd = GC.GetTotalMemory(false),
        };
        // Whatever isn't waiting on disk, model building or indexing is JSON tokenising/parsing.
        metrics.JsonParse = metrics.Total - metrics.FileRead - metrics.ModelAndGeometry - metrics.SpatialIndex;
        if (metrics.JsonParse < TimeSpan.Zero) metrics.JsonParse = TimeSpan.Zero;

        return new AorDataset(array, index, skipped, metrics);
    }
}
