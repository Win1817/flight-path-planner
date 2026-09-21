using System.Text.Json;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;
using FlightPathPlanner.Tests.TestData;
using FlightPathPlanner.ViewModels;
using NetTopologySuite.Geometries;

namespace FlightPathPlanner.Tests;

public class LargeOpsTests
{
    private static Task<OpsDataset> Import(string json, CancellationToken ct = default) =>
        OpsImporter.ImportAsync(ImportSource.FromText("ops.json", json), null, ct);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpsImporter_StreamsArraysAndWrappedPlans_LikeTheDomParser(bool wrapped)
    {
        var json = OpsDatasetGenerator.Generate(300, seed: 3, wrapped);
        var dom = OpsParser.ParseOps(JsonDocument.Parse(json).RootElement);

        var result = await Import(json);

        Assert.Equal(dom.Count, result.Items.Length);
        Assert.Equal(dom.Select(o => o.OperationPlanId), result.Items.Select(o => o.OperationPlanId));
        Assert.Equal(new[] { "EMERGENCY", "NOMINAL", "TIMEOUT", "WITHDRAWN" }, result.ClosureReasons);
        Assert.NotNull(result.OverallRange);
        Assert.All(result.Items, o => Assert.False(o.Bounds.IsNull));
    }

    [Fact]
    public async Task OpsImporter_SkipsMalformedPlans_AndContinues()
    {
        var result = await Import(OpsDatasetGenerator.Generate(100, seed: 4, malformedEvery: 10));

        Assert.Equal(90, result.Items.Length);
        Assert.Equal(10, result.Skipped);
        Assert.Equal(100, result.Metrics.Records);
    }

    [Fact]
    public async Task OpsImporter_AcceptsASinglePlanObject()
    {
        var single = OpsDatasetGenerator.Generate(1, seed: 5)[1..^1]; // strip the array brackets

        var result = await Import(single);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task OpsImporter_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Import(OpsDatasetGenerator.Generate(500, seed: 6), cts.Token));
    }

    [Fact]
    public async Task OpsTab_LargeImport_UsesVirtualRowsAndOffThreadFilters()
    {
        var vm = new OpsTabViewModel();
        var path = Path.Combine(Path.GetTempPath(), $"ops-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, OpsDatasetGenerator.Generate(5000, seed: 7));
        try
        {
            Assert.True(await vm.ImportAsync(ImportSource.FromFile(path), persist: false));

            Assert.Equal(5000, vm.TotalCount);
            Assert.Empty(vm.FilteredOps.MaterialisedRows);
            Assert.Equal(4, vm.ClosureReasonChips.Count);

            // closure-reason filter on a large set (background path)
            vm.ClosureReasonChips.Single(c => c.Reason == "NOMINAL").IsSelected = true;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (vm.FilteredCount == 5000 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.Equal(1250, vm.FilteredCount);
            Assert.All(vm.FilteredOpData, o => Assert.Equal("NOMINAL", o.ClosureReason));

            // text search
            vm.SearchQuery = "survey";
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (vm.FilteredCount == 1250 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.True(vm.FilteredCount is > 0 and < 1250);
            Assert.All(vm.FilteredOpData, o => Assert.Contains("survey", o.SearchText));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OpsTab_TimeframeFilter_KeepsTheOriginalOverlapSemantics()
    {
        var vm = new OpsTabViewModel();
        vm.LoadFromJson(OpsDatasetGenerator.Generate(200, seed: 8), "t.json", persist: false);

        vm.TimeframeFrom = new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero);
        vm.TimeframeTo = new DateTimeOffset(2025, 3, 31, 12, 0, 0, TimeSpan.Zero);

        Assert.True(vm.FilteredCount is > 0 and < 200);
        Assert.All(vm.FilteredOpData, o =>
        {
            Assert.True(o.StartTime <= new DateTimeOffset(2025, 3, 31, 23, 59, 59, 999, TimeSpan.Zero));
            Assert.True(o.EndTime >= new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero));
        });
    }

    [Fact]
    public async Task Payload_ForOps_SwitchesToViewportRenderingAboveTheThreshold()
    {
        var dataset = await Import(OpsDatasetGenerator.Generate(2500, seed: 9));
        var none = new HashSet<string>();

        var whole = MapPayloadBuilder.ForOps(dataset, null, 100, none, null, fit: true, 1, default);
        var byView = MapPayloadBuilder.ForOps(dataset, null, dataset.Items.Length, none, new Envelope(22, 23, 52, 53), fit: false, 2, default);

        Assert.False(whole.ViewportMode);
        Assert.True(byView.ViewportMode);
        Assert.InRange(byView.Shown, 1, dataset.Items.Length - 1);
        Assert.True(byView.Shown < dataset.Items.Length / 3, "only operations near the viewport are sent");
    }

    // ---- Report / lookup matching ----

    [Fact]
    public async Task ReportService_BoundsPrefilterDoesNotChangeResults()
    {
        var ops = (await Import(OpsDatasetGenerator.Generate(600, seed: 10))).Items;
        var aors = (await AorImporter.ImportAsync(ImportSource.FromText("a.json", GeoZoneDatasetGenerator.Generate(40, seed: 10)), null, default)).Items;

        foreach (var aor in aors.Take(15))
        {
            var fast = ReportService.GetOpsInAor(ops, aor).Select(o => o.OperationPlanId).ToHashSet();
            // Exact check with no bounds shortcut: rebuild the ops with empty (null) bounds so every op is a candidate.
            var unbounded = ops.Select(o => new ParsedOps
            {
                OperationPlanId = o.OperationPlanId, OperationVolumes = o.OperationVolumes, OffNominalVolumes = o.OffNominalVolumes,
                Bounds = new Envelope(-180, 180, -90, 90),
            }).ToList();
            var exact = ReportService.GetOpsInAor(unbounded, aor).Select(o => o.OperationPlanId).ToHashSet();
            Assert.Equal(exact, fast);
        }
    }

    [Fact]
    public async Task ReportService_ParallelSummaryMatchesPerAorCounts()
    {
        var ops = (await Import(OpsDatasetGenerator.Generate(400, seed: 11))).Items;
        var aors = (await AorImporter.ImportAsync(ImportSource.FromText("a.json", GeoZoneDatasetGenerator.Generate(120, seed: 11)), null, default)).Items;

        var summary = ReportService.GetAorReportSummary(aors, ops);

        Assert.Equal(aors.Length, summary.Count);
        for (int i = 0; i < aors.Length; i += 17)
            Assert.Equal(ReportService.GetOpsInAor(ops, aors[i]).Count, summary[i].MatchCount);
        Assert.True(summary.Any(r => r.MatchCount > 0));
    }

    [Fact]
    public async Task ReportExport_ComputesOnceAndCanBuildOnAWorkerThread()
    {
        var vm = new MainViewModel(null);
        vm.OpsTab.LoadFromJson(OpsDatasetGenerator.Generate(300, seed: 12), "ops.json", persist: false);
        vm.AorTab.LoadFromJson(GeoZoneDatasetGenerator.Generate(60, seed: 12), "aor.json", persist: false);

        var export = vm.ReportTab.PrepareSummaryExport();
        var json = await Task.Run(export.BuildJson);
        var xlsx = await Task.Run(export.BuildXlsx);

        Assert.Equal(60, JsonDocument.Parse(json).RootElement.GetProperty("data").GetArrayLength());
        Assert.True(xlsx.Length > 1000);
    }
}
