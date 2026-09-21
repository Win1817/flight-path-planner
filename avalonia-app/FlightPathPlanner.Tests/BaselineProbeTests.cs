using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FlightPathPlanner.Services;
using FlightPathPlanner.Tests.TestData;
using FlightPathPlanner.ViewModels;
using Xunit.Abstractions;

namespace FlightPathPlanner.Tests;

/// <summary>Phase 1 diagnosis: times each stage of the ORIGINAL pipeline (everything below ran on the UI thread).
/// Opt-in: set FPP_PROBE=1.</summary>
public class BaselineProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void OriginalPipelineStageTimings()
    {
        if (Environment.GetEnvironmentVariable("FPP_PROBE") != "1") return;
        foreach (var zones in new[] { 1000, 5000, 10000, 25000 }) Probe(zones); // sequential: parallel runs distort timings
    }

    private void Probe(int zones)
    {

        var path = Path.Combine(Path.GetTempPath(), $"probe-{zones}.json");
        GeoZoneDatasetGenerator.WriteToFile(path, zones);
        long bytes = new FileInfo(path).Length;
        var sw = Stopwatch.StartNew();
        double Lap() { var t = sw.Elapsed.TotalMilliseconds; sw.Restart(); return t; }

        var text = File.ReadAllText(path); var read = Lap();
        using var doc = JsonDocument.Parse(text); var parse = Lap();
        var parsed = AorParser.ParseAors(doc.RootElement); var model = Lap();
        var processed = parsed.Aors.Select((a, i) => AorParser.ProcessAor(a, i)).ToList(); var process = Lap();
        var vm = new AorTabViewModel();
        vm.LoadFromJson(text, "probe.json", persist: false); var wholeVm = Lap(); // parse+process+rows again (measures row creation by difference)
        var geo = MapGeoJson.Build(Array.Empty<Models.ParsedOps>(), processed); var geoMs = Lap();

        output.WriteLine($"zones={zones} file={bytes / 1048576.0:0.0}MB  read={read:0}ms  DOM parse={parse:0}ms  normalize={model:0}ms  process(area)={process:0}ms  " +
                         $"LoadFromJson total(all UI thread)={wholeVm:0}ms  GeoJSON build={geoMs:0}ms ({geo.Length / 1048576.0:0.0}MB string)  " +
                         $"managed={GC.GetTotalMemory(false) / 1048576}MB peakWS={Process.GetCurrentProcess().PeakWorkingSet64 / 1048576}MB");

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        var sw2 = Stopwatch.StartNew();
        var ds = Services.Import.AorImporter.ImportAsync(Services.Import.ImportSource.FromFile(path), null, default).GetAwaiter().GetResult();
        output.WriteLine($"   STREAMING importer: {sw2.Elapsed.TotalMilliseconds:0}ms  retained={(GC.GetTotalMemory(true) - before) / 1048576}MB  | {ds.Metrics.ToDiagnosticText()}");
        File.Delete(path);
    }
}
