using System.Collections.Specialized;
using System.Text.Json;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;
using FlightPathPlanner.Tests.TestData;
using FlightPathPlanner.ViewModels;
using NetTopologySuite.Geometries;

namespace FlightPathPlanner.Tests;

public sealed class LargeDatasetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fpp-large-" + Guid.NewGuid().ToString("N"));

    public LargeDatasetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ImportSource Zones(int count, int seed = 1, bool wrapped = false, int malformedEvery = 0)
    {
        var path = Path.Combine(_dir, $"zones-{count}-{seed}-{wrapped}.json");
        GeoZoneDatasetGenerator.WriteToFile(path, count, seed, wrapped, malformedEvery);
        return ImportSource.FromFile(path);
    }

    private static async Task<AorDataset> LoadDataset(ImportSource source) => await AorImporter.ImportAsync(source, null, default);

    // =================================== VirtualRowList ===================================

    [Fact]
    public void VirtualRowList_CreatesRowsOnlyWhenAsked_AndResetNotifiesOnce()
    {
        int created = 0;
        var list = new VirtualRowList<object>(pos => { created++; return new object(); });
        int resets = 0;
        list.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };

        list.Reset(Enumerable.Range(0, 50_000).ToArray());

        Assert.Equal(50_000, list.Count);
        Assert.Equal(0, created);
        Assert.Equal(1, resets);

        var a = list[10];
        Assert.Same(a, list[10]);          // cached
        Assert.Equal(1, created);
        _ = list[49_999];
        Assert.Equal(2, created);
    }

    [Fact]
    public void VirtualRowList_IndexesByPosition_AndSupportsNonGenericIList()
    {
        var list = new VirtualRowList<string>(pos => $"row{pos}");
        list.Reset(new[] { 3, 7, 11 });

        Assert.Equal("row7", list[1]);
        Assert.Equal("row11", list.RowAtPosition(11));
        Assert.Null(list.RowAtPosition(4));
        var nonGeneric = (System.Collections.IList)list;
        Assert.Equal("row3", nonGeneric[0]);
        Assert.Equal(1, nonGeneric.IndexOf(list[1]));
        Assert.Throws<NotSupportedException>(() => nonGeneric.Add("x"));
    }

    // =================================== Spatial index ===================================

    [Fact]
    public async Task SpatialIndex_ViewportQueryMatchesBruteForce()
    {
        var dataset = await LoadDataset(Zones(3000, seed: 4));
        var viewport = new Envelope(15, 22, 50, 55);

        var (indexes, candidates) = dataset.Index.Query(viewport, null, int.MaxValue);
        var brute = Enumerable.Range(0, dataset.Items.Length).Where(i => dataset.Items[i].Bounds.Intersects(viewport)).ToList();

        Assert.Equal(brute, indexes);
        Assert.Equal(brute.Count, candidates);
        Assert.InRange(candidates, 1, dataset.Items.Length - 1); // a real subset, not everything or nothing
    }

    [Fact]
    public async Task SpatialIndex_AppliesFilterMask_AndCapKeepsTheLargestFeatures()
    {
        var dataset = await LoadDataset(Zones(2000, seed: 5));
        var mask = new bool[dataset.Items.Length];
        for (int i = 0; i < mask.Length; i += 2) mask[i] = true;

        var (all, _) = dataset.Index.Query(null, mask, int.MaxValue);
        Assert.All(all, i => Assert.True(mask[i]));

        var (top, candidates) = dataset.Index.Query(null, mask, 100);
        Assert.Equal(100, top.Count);
        Assert.Equal(all.Count, candidates);
        double smallestKept = top.Min(i => dataset.Items[i].ComputedArea);
        double largestDropped = all.Except(top).Max(i => dataset.Items[i].ComputedArea);
        Assert.True(smallestKept >= largestDropped);

        Assert.Equal(dataset.Index.BoundsOfIncluded(null).MaxX, dataset.Items.Max(a => a.Bounds.MaxX));
    }

    // =================================== GeoJSON chunks ===================================

    [Fact]
    public async Task MapGeoJson_SplitsIntoChunks_QuantisesAndFlagsHighlights()
    {
        var dataset = await LoadDataset(Zones(1000, seed: 6));
        var highlighted = new HashSet<string> { dataset.Items[3].Id };

        var chunks = MapGeoJson.BuildChunks(Array.Empty<ParsedOps>(), dataset.Items, null, highlighted, chunkFeatures: 400);

        Assert.Equal(3, chunks.Count); // 400 + 400 + 200
        var counts = chunks.Select(c => JsonDocument.Parse(c).RootElement.GetArrayLength()).ToList();
        Assert.Equal(new[] { 400, 400, 200 }, counts);

        var first = JsonDocument.Parse(chunks[0]).RootElement[3];
        Assert.Equal(1, first.GetProperty("properties").GetProperty("hl").GetInt32());
        Assert.False(JsonDocument.Parse(chunks[0]).RootElement[4].GetProperty("properties").TryGetProperty("hl", out _));

        // Coordinates are rounded to 6 decimals (~0.1 m).
        var geometry = JsonDocument.Parse(chunks[0]).RootElement[0].GetProperty("geometry").GetProperty("coordinates");
        int checkedNumbers = 0;
        void Visit(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Array) { foreach (var c in e.EnumerateArray()) Visit(c); }
            else { Assert.Equal(Math.Round(e.GetDouble(), 6), e.GetDouble()); checkedNumbers++; }
        }
        Visit(geometry);
        Assert.True(checkedNumbers > 10);
    }

    [Fact]
    public void MapGeoJson_WholeCollectionForm_StillWorksForSmallInputs()
    {
        var json = MapGeoJson.Build(Array.Empty<ParsedOps>(), Array.Empty<ParsedAor>());

        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());
        Assert.Equal(0, root.GetProperty("features").GetArrayLength());
    }

    [Fact]
    public async Task MapGeoJson_HonoursCancellation()
    {
        var dataset = await LoadDataset(Zones(1500, seed: 7));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            MapGeoJson.BuildChunks(Array.Empty<ParsedOps>(), dataset.Items, null, null, ct: cts.Token));
    }

    // =================================== Viewport payloads ===================================

    [Fact]
    public async Task Payload_SmallSetIsSentWhole_LargeSetByViewport()
    {
        var small = await LoadDataset(Zones(300, seed: 8));
        var large = await LoadDataset(Zones(4000, seed: 8));
        var none = new HashSet<string>();

        var whole = MapPayloadBuilder.ForAors(small, null, small.Items.Length, none, null, fit: true, 1, default);
        Assert.False(whole.ViewportMode);
        Assert.Equal(300, whole.Shown);
        Assert.NotNull(whole.FitBounds);

        var europe = new Envelope(9, 31, 46, 61);
        var view = new Envelope(15, 18, 50, 52);
        var byViewport = MapPayloadBuilder.ForAors(large, null, large.Items.Length, none, view, fit: false, 2, default);

        Assert.True(byViewport.ViewportMode);
        Assert.Equal(4000, byViewport.Total);
        Assert.InRange(byViewport.Shown, 1, MapPayloadBuilder.MaxViewportFeatures);
        Assert.True(byViewport.Shown < large.Items.Length / 2, "only features near the viewport should be sent");
        Assert.Equal(byViewport.Shown, byViewport.InView); // under the cap: everything in view is drawn
        Assert.Null(byViewport.FitBounds);                 // panning must not move the camera
        _ = europe;
    }

    [Fact]
    public async Task Payload_CapsWhatIsDrawn_AndReportsHowManyWereInView()
    {
        var large = await LoadDataset(Zones(6000, seed: 9));
        var everything = new Envelope(-180, 180, -90, 90);

        var payload = MapPayloadBuilder.ForAors(large, null, large.Items.Length, new HashSet<string>(), everything, fit: false, 3, default);

        Assert.Equal(MapPayloadBuilder.MaxViewportFeatures, payload.Shown);
        Assert.Equal(6000, payload.InView);
        int features = payload.Chunks.Sum(c => JsonDocument.Parse(c).RootElement.GetArrayLength());
        Assert.Equal(MapPayloadBuilder.MaxViewportFeatures, features);
        Assert.True(payload.Chunks.Count > 1, "a large payload is delivered as several messages");
    }

    [Fact]
    public async Task Payload_FitInViewportMode_SendsFeaturesForTheTargetBounds_WithoutWaitingForTheMap()
    {
        var large = await LoadDataset(Zones(3000, seed: 10));

        var payload = MapPayloadBuilder.ForAors(large, null, large.Items.Length, new HashSet<string>(), viewport: null, fit: true, 4, default);

        Assert.True(payload.Fit);
        Assert.NotNull(payload.FitBounds);
        Assert.True(payload.Shown > 0);
        Assert.False(payload.NeedsViewport);
    }

    [Fact]
    public async Task Payload_UnknownViewportWithoutFit_AsksThePageForItsViewport()
    {
        var large = await LoadDataset(Zones(3000, seed: 11));

        var payload = MapPayloadBuilder.ForAors(large, null, large.Items.Length, new HashSet<string>(), viewport: null, fit: false, 5, default);

        Assert.Equal(0, payload.Shown);
        Assert.True(payload.NeedsViewport);
    }

    // =================================== AoR tab: async import ===================================

    [Fact]
    public async Task AorImport_RunsToCompletion_WithoutMaterialisingRows_AndKeepsAStreamedCopy()
    {
        var storage = new LocalStorageService(Path.Combine(_dir, "data"));
        var vm = new AorTabViewModel(storage);
        var source = Zones(5000, seed: 12);
        var states = new List<ImportState>();
        vm.Import.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ImportViewModel.State)) states.Add(vm.Import.State); };

        bool ok = await vm.ImportAsync(source, persist: true);

        Assert.True(ok);
        Assert.Equal(new[] { ImportState.Importing, ImportState.Completed }, states);
        Assert.Equal(5000, vm.TotalCount);
        Assert.Equal(5000, vm.FilteredAors.Count);
        Assert.Empty(vm.FilteredAors.MaterialisedRows);           // 5,000 zones, zero row view models so far
        Assert.Contains("Import complete", vm.Import.SummaryTitle);
        Assert.Contains(vm.Import.SummaryLines, l => l.StartsWith("Records: 5,000"));

        var saved = Assert.Single(storage.List(StorageCategory.Aor, archived: false));
        Assert.Equal(new FileInfo(source.Name == null ? "" : Path.Combine(_dir, $"zones-5000-12-False.json")).Length, saved.SizeBytes);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "data", "AoR"), "*.partial")); // atomic: no temp file left behind
    }

    [Fact]
    public async Task AorImport_CanBeCancelled_LeavingThePreviousDatasetUntouched()
    {
        var vm = new AorTabViewModel();
        Assert.True(vm.LoadFromJson(GeoZoneDatasetGenerator.Generate(25, seed: 1), "before.json", persist: false));

        var task = vm.ImportAsync(Zones(25_000, seed: 13), persist: false);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (vm.Import.CountText == "" && DateTime.UtcNow < deadline) await Task.Delay(5); // wait for the first progress report
        vm.Import.CancelCommand.Execute(null);
        bool ok = await task;

        Assert.False(ok);
        Assert.Equal(ImportState.Cancelled, vm.Import.State);
        Assert.Equal("Import cancelled", vm.Import.SummaryTitle);
        Assert.Equal(25, vm.TotalCount);                 // no partial data
        Assert.Equal("before.json", vm.UploadedFileName);
    }

    [Fact]
    public async Task AorImport_Failure_ReportsAndKeepsPreviousData()
    {
        var vm = new AorTabViewModel();
        vm.LoadFromJson(GeoZoneDatasetGenerator.Generate(10, seed: 2), "keep.json", persist: false);
        var badPath = Path.Combine(_dir, "bad.json");
        await File.WriteAllTextAsync(badPath, "[{\"identifier\":\"X\",\"name\":\"Y\"");

        bool ok = await vm.ImportAsync(ImportSource.FromFile(badPath), persist: false);

        Assert.False(ok);
        Assert.Equal(ImportState.Failed, vm.Import.State);
        Assert.Equal(10, vm.TotalCount);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task AorImport_SkippedRecords_AreSummarisedNotDumped()
    {
        var vm = new AorTabViewModel();

        await vm.ImportAsync(Zones(1000, seed: 14, malformedEvery: 10), persist: false);

        Assert.Equal(900, vm.TotalCount);
        Assert.True(vm.Import.SummaryHasWarnings);
        Assert.Equal("Import complete with warnings", vm.Import.SummaryTitle);
        Assert.Contains(vm.Import.SummaryLines, l => l.Contains("100 records could not be parsed"));
        Assert.True(vm.Import.SummaryLines.Count <= 6); // a concise summary, not one line per bad record
    }

    [Fact]
    public async Task AorImport_ASecondImportCannotStartWhileOneRuns()
    {
        var vm = new AorTabViewModel();
        var first = vm.ImportAsync(Zones(20_000, seed: 15), persist: false);
        while (!vm.Import.IsActive) await Task.Delay(1);

        Assert.False(await vm.ImportAsync(Zones(10, seed: 16), persist: false));

        vm.Import.CancelCommand.Execute(null);
        await first;
    }

    [Fact]
    public async Task LargeFiles_AskForConfirmationBeforeImporting()
    {
        var vm = new AorTabViewModel();
        var small = Zones(20, seed: 17);
        var pretendHuge = new ImportSource("huge.json", AorTabViewModel.LargeFileBytes + 1, small.Open);

        bool started = await vm.RequestImportAsync(pretendHuge);

        Assert.False(started);
        Assert.True(vm.HasPendingLargeSource);
        Assert.Equal(0, vm.TotalCount);
        Assert.Contains("huge.json", vm.PendingLargeText);

        vm.ConfirmLargeImportCommand.Execute(null);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (vm.TotalCount == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);

        Assert.False(vm.HasPendingLargeSource);
        Assert.Equal(20, vm.TotalCount);
    }

    // =================================== AoR tab: filter, selection, removal ===================================

    [Fact]
    public async Task AorFilter_OnALargeSet_IsDebouncedAndAppliedAsOneUpdate()
    {
        var vm = new AorTabViewModel();
        await vm.ImportAsync(Zones(6000, seed: 18), persist: false);
        Assert.True(vm.TotalCount > AorTabViewModel.ImmediateFilterLimit);
        int resets = 0;
        vm.FilteredAors.CollectionChanged += (_, _) => resets++;
        int expected = vm.AllAors.Count(a => a.SearchText.Contains("notam"));

        foreach (var typed in new[] { "n", "no", "not", "nota", "notam" }) vm.SearchQuery = typed; // a burst of keystrokes
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.FilteredCount != expected && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.Equal(expected, vm.FilteredCount);
        Assert.True(resets <= 2, $"typing 5 letters caused {resets} list resets");
        Assert.True(vm.FilterMask != null);
    }

    [Fact]
    public async Task AorSelection_LivesInASet_SoSelectAllIsInstantAndCountsOnlyVisibleItems()
    {
        var vm = new AorTabViewModel();
        await vm.ImportAsync(Zones(3000, seed: 19), persist: false);

        vm.ToggleSelectAllCommand.Execute(null);
        Assert.Equal(3000, vm.SelectedCount);
        Assert.True(vm.AllSelected);
        Assert.Empty(vm.FilteredAors.MaterialisedRows); // selecting 3,000 items created no rows

        vm.SearchQuery = "notam"; // instant only up to the debounce limit; wait for the large-set path
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.FilteredCount == 3000 && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.Equal(vm.FilteredCount, vm.SelectedCount); // selection outside the filter isn't counted
        Assert.Equal(vm.FilteredCount, vm.SelectedAors.Count);

        vm.ToggleSelectAllCommand.Execute(null);          // deselect the visible ones
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task AorRowCheckbox_UpdatesTheSelectionSet_AndSurvivesScrollingRowsBackIn()
    {
        var vm = new AorTabViewModel();
        await vm.ImportAsync(Zones(200, seed: 20), persist: false);

        vm.FilteredAors[5].IsSelected = true;
        Assert.Equal(1, vm.SelectedCount);

        vm.SearchQuery = "zone";                          // every name contains "Zone": a re-filter that keeps all rows but discards the row objects
        Assert.Empty(vm.FilteredAors.MaterialisedRows);
        Assert.True(vm.FilteredAors[5].IsSelected);       // the fresh row reads the selection set
    }

    [Fact]
    public async Task AorRemoval_RebuildsTheDataset_AndKeepsTheSurvivingSelection()
    {
        var vm = new AorTabViewModel();
        await vm.ImportAsync(Zones(100, seed: 21), persist: false);
        var keepId = vm.AllAors[50].Name;
        vm.FilteredAors[50].IsSelected = true;   // selected survivor
        vm.FilteredAors[10].IsSelected = true;   // selected, about to be removed

        vm.DeleteAorCommand.Execute(vm.FilteredAors[10]);

        Assert.Equal(99, vm.TotalCount);
        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal(keepId, vm.SelectedAors.Single().Name);
        Assert.Equal(Enumerable.Range(0, 99), vm.AllAors.Select(a => a.Ordinal));
        Assert.Equal(99, vm.Dataset.Index.Count);
    }

    // =================================== Storage ===================================

    [Fact]
    public async Task SaveStream_CopiesLargeStreamsAtomically_AndCleansUpOnFailure()
    {
        var storage = new LocalStorageService(Path.Combine(_dir, "store"));
        var source = Zones(2000, seed: 22);

        var saved = await storage.SaveStreamAsync(StorageCategory.Aor, "big.json", source.Open);
        Assert.Equal(source.Length, saved.SizeBytes);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "store", "AoR"), "*.partial"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveStreamAsync(StorageCategory.Aor, "boom.json", () => throw new InvalidOperationException("no source")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveStreamAsync(StorageCategory.Aor, "cancel.json", source.Open, cts.Token));

        Assert.Single(storage.List(StorageCategory.Aor, archived: false));              // only the good copy is listed
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "store", "AoR"), "*.partial"));
    }

    [Fact]
    public async Task SavedFile_CanBeReloadedThroughTheStreamingImporter()
    {
        var storage = new LocalStorageService(Path.Combine(_dir, "reload"));
        var vm = new AorTabViewModel(storage);
        await vm.ImportAsync(Zones(500, seed: 23), persist: true);
        vm.DeleteSelectedCommand.Execute(null);
        vm.ToggleSelectAllCommand.Execute(null);
        vm.DeleteSelectedCommand.Execute(null);
        Assert.Equal(0, vm.TotalCount);

        await vm.SavedFiles.LoadAsync(vm.SavedFiles.Rows[0]);

        Assert.Equal(500, vm.TotalCount);
        Assert.Equal(1, vm.SavedFiles.ActiveCount); // reloading must not save another copy
    }
}
