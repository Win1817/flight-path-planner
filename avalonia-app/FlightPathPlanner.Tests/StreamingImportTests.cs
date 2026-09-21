using System.Text;
using System.Text.Json;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;
using FlightPathPlanner.Tests.TestData;

namespace FlightPathPlanner.Tests;

public class StreamingImportTests
{
    private static Task<AorDataset> Import(string json, CancellationToken ct = default, Action<ImportProgress>? progress = null) =>
        AorImporter.ImportAsync(ImportSource.FromText("test.json", json), progress, ct);

    // ---------- JsonStreamReader ----------

    [Theory]
    [InlineData(1024)]   // minimum buffer: every record straddles several reads
    [InlineData(4096)]
    [InlineData(1 << 16)]
    public async Task ReadArrayElements_AreIdenticalRegardlessOfBufferSize(int bufferBytes)
    {
        var json = GeoZoneDatasetGenerator.Generate(60, seed: 7);
        var expected = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.GetProperty("identifier").GetString()).ToList();

        using var reader = new JsonStreamReader(new MemoryStream(Encoding.UTF8.GetBytes(json)), bufferBytes);
        Assert.True(await reader.ReadAsync(default));
        Assert.Equal(JsonTokenType.StartArray, reader.TokenType);

        var actual = new List<string?>();
        while (true)
        {
            using var doc = await reader.ReadArrayElementAsync(default);
            if (doc == null) break;
            actual.Add(doc.RootElement.GetProperty("identifier").GetString());
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Reader_HandlesValuesLargerThanTheBuffer_AndSkipsContainers()
    {
        var big = new string('x', 50_000);
        var json = $$"""{"a":{"deep":[1,2,{"k":"{{big}}"}]},"b":[{"id":1},{"id":2}],"c":3}""";
        using var reader = new JsonStreamReader(new MemoryStream(Encoding.UTF8.GetBytes(json)), 1024);

        Assert.True(await reader.ReadAsync(default));                    // {
        Assert.True(await reader.ReadAsync(default));                    // "a"
        Assert.Equal("a", reader.PropertyName);
        await reader.SkipValueAsync(default);                            // whole "a" value, 50 KB, through a 1 KB buffer
        Assert.True(await reader.ReadAsync(default));                    // "b"
        Assert.Equal("b", reader.PropertyName);
        Assert.True(await reader.ReadAsync(default));                    // [
        using var first = await reader.ReadArrayElementAsync(default);
        Assert.Equal(1, first!.RootElement.GetProperty("id").GetInt32());
        first.Dispose();
        using var second = await reader.ReadArrayElementAsync(default);
        Assert.Equal(2, second!.RootElement.GetProperty("id").GetInt32());
        second.Dispose();
        Assert.Null(await reader.ReadArrayElementAsync(default));        // ]
    }

    [Fact]
    public async Task Reader_ReportsInvalidJson()
    {
        using var reader = new JsonStreamReader(new MemoryStream(Encoding.UTF8.GetBytes("[{\"a\":1},{\"a\":")), 1024);
        await reader.ReadAsync(default);
        using var ok = await reader.ReadArrayElementAsync(default);
        ok!.Dispose();
        await Assert.ThrowsAnyAsync<JsonException>(async () => { using var _ = await reader.ReadArrayElementAsync(default); });
    }

    // ---------- Importer: layouts & equivalence with the DOM parser ----------

    [Fact]
    public async Task Importer_MatchesTheDomParser_OnAllThreeLayouts()
    {
        var array = GeoZoneDatasetGenerator.Generate(200, seed: 3);
        var wrapped = GeoZoneDatasetGenerator.Generate(200, seed: 3, wrapInObject: true);

        var dom = AorParser.ParseAors(JsonDocument.Parse(array).RootElement).Aors;
        var fromArray = await Import(array);
        var fromWrapped = await Import(wrapped);

        Assert.Equal(dom.Count, fromArray.Items.Length);
        Assert.Equal(dom.Select(a => a.Id), fromArray.Items.Select(a => a.Id));
        Assert.Equal(dom.Select(a => a.Name), fromWrapped.Items.Select(a => a.Name));
        Assert.Equal(dom.Sum(a => a.Geometry.ComputeArea()), fromArray.Items.Sum(a => a.ComputedArea), precision: 3);
    }

    [Fact]
    public async Task Importer_AcceptsASingleRecordObject_ViaTheFallbackPass()
    {
        const string single = """{"identifier":"ONE","name":"Only Zone","geometry":[{"horizontalProjection":{"type":"Polygon","coordinates":[[[25,54],[26,54],[26,55],[25,55],[25,54]]]},"lowerLimit":0,"upperLimit":100}]}""";

        var result = await Import(single);

        Assert.Single(result.Items);
        Assert.Equal("Only Zone", result.Items[0].Name);
    }

    [Fact]
    public async Task Importer_SkipsMalformedRecords_AndCountsThem()
    {
        var json = GeoZoneDatasetGenerator.Generate(100, seed: 5, malformedEvery: 10);

        var result = await Import(json);

        Assert.Equal(90, result.Items.Length);
        Assert.Equal(10, result.Skipped);
        Assert.Equal(100, result.Metrics.Records);
        Assert.Equal(90, result.Metrics.Valid);
        Assert.Equal(Enumerable.Range(0, 90), result.Items.Select(a => a.Ordinal));
    }

    [Fact]
    public async Task Importer_PrecomputesBoundsAndSearchText()
    {
        var result = await Import(GeoZoneDatasetGenerator.Generate(20, seed: 9));

        Assert.All(result.Items, a =>
        {
            Assert.False(a.Bounds.IsNull);
            Assert.Contains(a.Name.ToLowerInvariant(), a.SearchText);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("42")]
    [InlineData("[{\"nothing\":\"useful\"}]")]
    [InlineData("{\"zones\":[]}")]
    public async Task Importer_RejectsFilesWithNoUsableZones(string json)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Import(json));
    }

    [Fact]
    public async Task Importer_FailsCleanlyOnTruncatedJson()
    {
        var json = GeoZoneDatasetGenerator.Generate(50, seed: 2);
        await Assert.ThrowsAnyAsync<JsonException>(() => Import(json[..(json.Length / 2)]));
    }

    // ---------- Cancellation & progress ----------

    [Fact]
    public async Task Importer_CanBeCancelled_MidStream()
    {
        var json = GeoZoneDatasetGenerator.Generate(2000, seed: 11);
        using var cts = new CancellationTokenSource();
        int reports = 0;

        var task = Import(json, cts.Token, p => { if (++reports == 1) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Importer_ReportsMonotonicProgress_AndFinishesAtTheEnd()
    {
        var json = GeoZoneDatasetGenerator.Generate(3000, seed: 13);
        var reports = new List<ImportProgress>();

        var result = await AorImporter.ImportAsync(ImportSource.FromText("p.json", json), reports.Add, default);

        Assert.NotEmpty(reports);
        Assert.Equal(ImportStage.Reading, reports[0].Stage);
        for (int i = 1; i < reports.Count; i++) Assert.True(reports[i].BytesRead >= reports[i - 1].BytesRead);
        Assert.Equal(result.Items.Length, reports[^1].RecordsProcessed);
        Assert.InRange(reports.Count, 2, 200); // throttled: nowhere near one report per record
    }

    [Fact]
    public void ThrottledProgress_DropsIntermediateUpdates_ButFlushKeepsTheLatest()
    {
        var seen = new List<int>();
        var throttle = new ThrottledProgress(p => seen.Add(p.RecordsProcessed), TimeSpan.FromHours(1));

        throttle.Report(new ImportProgress(ImportStage.Parsing, 0, 100, 1, 0, TimeSpan.Zero), force: true);
        for (int i = 2; i <= 1000; i++) throttle.Report(new ImportProgress(ImportStage.Parsing, i, 1000, i, 0, TimeSpan.Zero));
        throttle.Flush();

        Assert.Equal(new[] { 1, 1000 }, seen);
    }

    [Fact]
    public void Progress_ComputesFractionAndEstimate()
    {
        var p = new ImportProgress(ImportStage.Parsing, 250, 1000, 10, 0, TimeSpan.FromSeconds(10));

        Assert.Equal(0.25, p.Fraction);
        Assert.Equal(TimeSpan.FromSeconds(30), p.EstimatedRemaining);
        Assert.Null(new ImportProgress(ImportStage.Parsing, 5, 1000, 1, 0, TimeSpan.FromMilliseconds(100)).EstimatedRemaining);
    }
}
