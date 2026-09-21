using System.Diagnostics;

namespace FlightPathPlanner.Services.Import;

public enum ImportState { Idle, Importing, Completed, Cancelled, Failed }

public enum ImportStage
{
    Reading,
    Parsing,
    Indexing,
    PreparingMap,
    Finishing,
}

/// <summary>A point-in-time snapshot of an import, safe to hand to the UI.</summary>
public sealed record ImportProgress(ImportStage Stage, long BytesRead, long TotalBytes, int RecordsProcessed, int RecordsSkipped, TimeSpan Elapsed)
{
    /// <summary>0..1 when the input size is known, otherwise null.</summary>
    public double? Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesRead / TotalBytes, 0, 1) : null;

    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (Fraction is not { } f || f < 0.03 || Elapsed < TimeSpan.FromSeconds(1)) return null;
            return TimeSpan.FromTicks((long)(Elapsed.Ticks * (1 - f) / f));
        }
    }
}

/// <summary>Timings and counts for one import, used for the completion summary and diagnostics.</summary>
public sealed class ImportMetrics
{
    public long FileSizeBytes { get; set; }
    public int Records { get; set; }
    public int Valid { get; set; }
    public int Skipped { get; set; }
    public long Polygons { get; set; }
    public long Vertices { get; set; }
    public TimeSpan FileRead { get; set; }
    public TimeSpan JsonParse { get; set; }
    public TimeSpan ModelAndGeometry { get; set; }
    public TimeSpan SpatialIndex { get; set; }
    public TimeSpan Total { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public long ManagedBytesAtEnd { get; set; }
    public string Description { get; set; } = "";

    public static long CurrentPeakWorkingSet()
    {
        try { return Process.GetCurrentProcess().PeakWorkingSet64; }
        catch { return 0; }
    }

    public string ToDiagnosticText() =>
        $"{Description}: {Records:N0} records ({Valid:N0} valid, {Skipped:N0} skipped), {FileSizeBytes / 1048576.0:0.0} MB, {Polygons:N0} polygons/{Vertices:N0} vertices | " +
        $"read {FileRead.TotalSeconds:0.00}s, parse {JsonParse.TotalSeconds:0.00}s, model+geometry {ModelAndGeometry.TotalSeconds:0.00}s, index {SpatialIndex.TotalSeconds:0.00}s, " +
        $"total {Total.TotalSeconds:0.00}s | peak working set {PeakWorkingSetBytes / 1048576} MB";
}

/// <summary>Where an import reads from. The stream can be opened more than once (some single-object files need a second pass).</summary>
public sealed class ImportSource(string name, long length, Func<Stream> open)
{
    public string Name { get; } = name;
    public long Length { get; } = length;
    public Stream Open() => open();

    public static ImportSource FromFile(string path) =>
        new(Path.GetFileName(path), new FileInfo(path).Length, () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true));

    public static ImportSource FromText(string name, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return new ImportSource(name, bytes.Length, () => new MemoryStream(bytes, writable: false));
    }
}

/// <summary>Progress sink that forwards at most one update per interval, so a fast import doesn't flood the UI with per-record
/// notifications. The final update is always delivered through <see cref="Flush"/>.</summary>
public sealed class ThrottledProgress(Action<ImportProgress> sink, TimeSpan? interval = null)
{
    private readonly long _intervalTicks = (interval ?? TimeSpan.FromMilliseconds(120)).Ticks;
    private long _lastReport = Stopwatch.GetTimestamp();
    private ImportProgress? _pending;

    public void Report(ImportProgress progress, bool force = false)
    {
        _pending = progress;
        long now = Stopwatch.GetTimestamp();
        if (!force && (now - _lastReport) * TimeSpan.TicksPerSecond / Stopwatch.Frequency < _intervalTicks) return;
        _lastReport = now;
        sink(progress);
        _pending = null;
    }

    public void Flush()
    {
        if (_pending is { } p) { sink(p); _pending = null; }
    }
}
