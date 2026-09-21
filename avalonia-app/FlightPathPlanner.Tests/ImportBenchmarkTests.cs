using System.Diagnostics;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;
using FlightPathPlanner.Tests.TestData;
using FlightPathPlanner.ViewModels;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace FlightPathPlanner.Tests;

/// <summary>Phase 9 benchmark. Opt-in (FPP_BENCH=1) so normal test runs stay fast:
///   FPP_BENCH=1 dotnet test --filter FullyQualifiedName~ImportBenchmark --logger "console;verbosity=detailed"
/// FPP_BENCH_SIZES overrides the zone counts (comma separated).</summary>
public class ImportBenchmarkTests(ITestOutputHelper output)
{
    /// <summary>Samples the process working set while an action runs, giving a peak for THAT action (Process.PeakWorkingSet64 is
    /// process-lifetime, so it can't separate one benchmark size from the next).</summary>
    private sealed class MemorySampler : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;
        public long PeakBytes { get; private set; }

        public MemorySampler()
        {
            var p = Process.GetCurrentProcess();
            _task = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    p.Refresh();
                    PeakBytes = Math.Max(PeakBytes, p.WorkingSet64);
                    try { await Task.Delay(15, _cts.Token); } catch (OperationCanceledException) { }
                }
            });
        }

        public void Dispose() { _cts.Cancel(); _task.Wait(); }
    }

    private static double Ms(Stopwatch sw) => sw.Elapsed.TotalMilliseconds;

    [Fact]
    public async Task GeoZonePipelineBenchmark()
    {
        if (Environment.GetEnvironmentVariable("FPP_BENCH") != "1") return;
        var sizes = (Environment.GetEnvironmentVariable("FPP_BENCH_SIZES") ?? "1000,5000,10000,25000,50000").Split(',').Select(int.Parse);

        output.WriteLine("GeoZone import benchmark (AoR schema, synthetic but schema-faithful; skewed geometry sizes)");
        foreach (var zones in sizes)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bench-{zones}.json");
            GeoZoneDatasetGenerator.WriteToFile(path, zones, malformedEvery: 1000);
            var source = ImportSource.FromFile(path);

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long baseline = GC.GetTotalMemory(true);

            AorDataset dataset;
            long peak;
            var total = Stopwatch.StartNew();
            using (var sampler = new MemorySampler())
            {
                dataset = await AorImporter.ImportAsync(source, null, default);
                total.Stop();
                await Task.Delay(40);
                peak = sampler.PeakBytes;
            }
            long retained = GC.GetTotalMemory(true) - baseline;
            var m = dataset.Metrics;

            // search across the whole dataset (what one debounced keystroke costs)
            var vm = new AorTabViewModel();
            var filterTimer = Stopwatch.StartNew();
            int matches = dataset.Items.Count(a => a.SearchText.Contains("notam", StringComparison.Ordinal));
            filterTimer.Stop();

            // what the map is asked to draw for a typical zoomed-in view vs. everything
            var view = new Envelope(15, 19, 50, 53);
            var mapTimer = Stopwatch.StartNew();
            var payload = MapPayloadBuilder.ForAors(dataset, null, dataset.Items.Length, new HashSet<string>(), view, fit: false, 1, default);
            mapTimer.Stop();
            long payloadBytes = payload.Chunks.Sum(c => (long)c.Length);

            var everythingTimer = Stopwatch.StartNew();
            long everythingBytes = 0;
            if (zones <= 25_000)
            {
                var all = MapGeoJson.BuildChunks(Array.Empty<Models.ParsedOps>(), dataset.Items, null, null, chunkFeatures: int.MaxValue);
                everythingBytes = all.Sum(c => (long)c.Length);
            }
            everythingTimer.Stop();

            output.WriteLine(
                $"\n[{zones:N0} zones] file {m.FileSizeBytes / 1048576.0:0.0} MB, {m.Polygons:N0} polygons / {m.Vertices:N0} vertices, valid {m.Valid:N0}, skipped {m.Skipped:N0}\n" +
                $"  import total      {total.Elapsed.TotalSeconds,6:0.00} s   (file read {m.FileRead.TotalSeconds:0.00} | JSON tokenise/parse {m.JsonParse.TotalSeconds:0.00} | model+geometry {m.ModelAndGeometry.TotalSeconds:0.00} | spatial index {m.SpatialIndex.TotalSeconds:0.00})\n" +
                $"  memory            peak working set +{(peak - baseline) / 1048576} MB during import; dataset retains {retained / 1048576} MB\n" +
                $"  search (all zones) {Ms(filterTimer):0.0} ms for {matches:N0} matches (runs off the UI thread above 2,000 items)\n" +
                $"  map, zoomed view  builds {payload.Shown:N0} of {payload.InView:N0} in-view features in {Ms(mapTimer):0} ms → {payloadBytes / 1048576.0:0.00} MB in {payload.Chunks.Count} chunk(s)\n" +
                (zones <= 25_000
                    ? $"  map, ALL zones    (old behaviour) {Ms(everythingTimer):0} ms → {everythingBytes / 1048576.0:0.0} MB in one message"
                    : "  map, ALL zones    (old behaviour) skipped at this size"));
            File.Delete(path);
        }
    }

    /// <summary>Writes sample files for manual/UI testing: FPP_GEN=/some/dir dotnet test --filter GenerateSampleFiles</summary>
    [Fact]
    public void GenerateSampleFiles()
    {
        var dir = Environment.GetEnvironmentVariable("FPP_GEN");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var n in new[] { 1000, 25000, 50000 })
            GeoZoneDatasetGenerator.WriteToFile(Path.Combine(dir, $"zones-{n}.json"), n, malformedEvery: 1000);
        File.WriteAllText(Path.Combine(dir, "ops-5000.json"), OpsDatasetGenerator.Generate(5000, malformedEvery: 500));
    }

    [Fact]
    public async Task OpsPipelineBenchmark()
    {
        if (Environment.GetEnvironmentVariable("FPP_BENCH") != "1") return;

        foreach (var count in new[] { 5000, 25000 })
        {
            var path = Path.Combine(Path.GetTempPath(), $"bench-ops-{count}.json");
            await File.WriteAllTextAsync(path, OpsDatasetGenerator.Generate(count, malformedEvery: 500));
            var source = ImportSource.FromFile(path);
            var timer = Stopwatch.StartNew();
            var dataset = await OpsImporter.ImportAsync(source, null, default);
            timer.Stop();
            output.WriteLine($"\n[OPS {count:N0}] file {source.Length / 1048576.0:0.0} MB, valid {dataset.Items.Length:N0}, skipped {dataset.Skipped}: import {timer.Elapsed.TotalSeconds:0.00} s ({dataset.Metrics.ToDiagnosticText()})");
            File.Delete(path);
        }
    }
}
