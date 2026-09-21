using FlightPathPlanner.Services;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Tests;

public sealed class LunaShellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fpp-shell-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string OpsJson = """
    [{"operationPlanId":"OP-1","state":"ACTIVE","operator":"A","closureReason":"NOMINAL","publicInfo":{"title":"One","description":""},
      "operationVolumes":[{"timeBegin":"2026-09-18T08:00:00.000Z","timeEnd":"2026-09-18T09:00:00.000Z","ordinal":0,
      "operationGeometry":{"geom":{"type":"Polygon","coordinates":[[[25.08,54.365],[25.09,54.365],[25.09,54.37],[25.08,54.37],[25.08,54.365]]]}}}]},
     {"operationPlanId":"OP-2","state":"ACTIVE","operator":"B","closureReason":"NOMINAL","publicInfo":{"title":"Two","description":""},
      "operationVolumes":[{"timeBegin":"2026-09-18T10:00:00.000Z","timeEnd":"2026-09-18T11:00:00.000Z","ordinal":0,
      "operationGeometry":{"geom":{"type":"Polygon","coordinates":[[[21.1,55.7],[21.11,55.7],[21.11,55.71],[21.1,55.71],[21.1,55.7]]]}}}]}]
    """;

    private const string AorJson = """
    {"identifier":"Z1","name":"Zone One","geometry":[{"horizontalProjection":{"type":"Polygon","coordinates":[[[25,54],[26,54],[26,55],[25,55],[25,54]]]}}]}
    """;

    [Fact]
    public void Header_ReflectsRealDataAndActiveSection()
    {
        var vm = new MainViewModel(null);
        Assert.Equal("Operations", vm.SectionTitle);
        Assert.Equal("No operations loaded", vm.SectionSubtitle);
        Assert.False(vm.HasAnyData);

        vm.OpsTab.LoadFromJson(OpsJson, "hdr-ops.json");
        vm.AorTab.LoadFromJson(AorJson, "hdr-aor.json");

        Assert.Equal("2 operations loaded", vm.SectionSubtitle);
        Assert.Equal("2 OPS", vm.OpsPillText);
        Assert.Equal("1 AoRs", vm.AorPillText);
        Assert.Equal("2", vm.OpsBadge);
        Assert.True(vm.HasAnyData);

        vm.OpsTab.SearchQuery = "One";
        Assert.Equal("1 of 2 operations shown", vm.SectionSubtitle);

        vm.ActiveTab = AppTab.Aors;
        Assert.Equal("Areas of Responsibility", vm.SectionTitle);
        Assert.Equal("1 areas loaded", vm.SectionSubtitle);
    }

    [Fact]
    public void ActiveFlags_FollowTheSelectedTab()
    {
        var vm = new MainViewModel(null);
        vm.ActiveTab = AppTab.Lookup;

        Assert.True(vm.IsLookupActive);
        Assert.False(vm.IsOpsActive);
        Assert.Equal("Search an address or coordinates", vm.SectionSubtitle);

        vm.ActiveTab = AppTab.Saved;
        Assert.True(vm.IsSavedActive);
        Assert.Equal("Saved Locally", vm.SectionTitle);
    }

    [Fact]
    public void ReportHeader_ExplainsMissingData_ThenShowsSelectionAndMatches()
    {
        var vm = new MainViewModel(null);
        vm.ActiveTab = AppTab.Report;
        Assert.Equal("Load OPS and AoR data to build a report", vm.SectionSubtitle);
        Assert.Equal("Upload both OPS and AoR data to generate a report.", vm.ReportTab.MissingDataText);

        vm.OpsTab.LoadFromJson(OpsJson, "rep-ops.json");
        Assert.Equal("Upload AoR data to generate a report.", vm.ReportTab.MissingDataText);
        vm.AorTab.LoadFromJson(AorJson, "rep-aor.json");
        Assert.Equal("1 AoRs available", vm.SectionSubtitle);

        vm.ReportTab.SelectedAor = vm.ReportTab.AorOptions[0];
        Assert.Equal("Zone One · 1 matching OPS", vm.SectionSubtitle);
    }

    [Fact]
    public void Notifications_ReportSuccessWarningAndErrors()
    {
        var seen = new List<(NotificationKind Kind, string Message)>();
        void Capture(NotificationKind k, string m) { lock (seen) seen.Add((k, m)); }
        Notifier.Posted += Capture;
        try
        {
            var vm = new MainViewModel(null);
            vm.OpsTab.LoadFromJson(OpsJson, "note-ops.json");
            vm.OpsTab.LoadFromJson("not json", "note-bad.json");
            vm.AorTab.LoadFromJson("""[{"identifier":"Z1","name":"Zone One","geometry":[{"horizontalProjection":{"type":"Polygon","coordinates":[[[25,54],[26,54],[26,55],[25,55],[25,54]]]}}]},{"name":"broken"}]""", "note-aor.json");

            lock (seen)
            {
                Assert.Contains(seen, n => n.Kind == NotificationKind.Success && n.Message.Contains("2 operations") && n.Message.Contains("note-ops.json"));
                Assert.Contains(seen, n => n.Kind == NotificationKind.Error && n.Message.Contains("note-bad.json"));
                Assert.Contains(seen, n => n.Kind == NotificationKind.Warning && n.Message.Contains("note-aor.json") && n.Message.Contains("skipped"));
            }
        }
        finally { Notifier.Posted -= Capture; }
    }

    [Fact]
    public void ToastQueue_KeepsNewestThree_AndDismisses()
    {
        var toasts = new ToastCenterViewModel(listenToNotifier: false); // isolated from tests posting concurrently
        for (int i = 1; i <= 5; i++) toasts.Show(NotificationKind.Info, $"toast-queue-{i}");

        Assert.Equal(ToastCenterViewModel.MaxVisible, toasts.Toasts.Count);
        Assert.Equal("toast-queue-3", toasts.Toasts[0].Message);
        Assert.Equal("toast-queue-5", toasts.Toasts[^1].Message);

        toasts.Dismiss(toasts.Toasts[0]);
        Assert.Equal(2, toasts.Toasts.Count);
        Assert.True(toasts.Toasts[0].IsInfo);
        Assert.Equal("Information", toasts.Toasts[0].KindLabel);
    }

    [Fact]
    public async Task ReloadingASavedFile_SwitchesToItsTab_AndSavedBadgeCountsFiles()
    {
        var vm = new MainViewModel(new LocalStorageService(_root));
        vm.OpsTab.LoadFromJson(OpsJson, "saved-ops.json");
        vm.AorTab.LoadFromJson(AorJson, "saved-aor.json");

        Assert.Equal("2", vm.SavedBadge);
        Assert.True(vm.HasSavedBadge);

        vm.ActiveTab = AppTab.Saved;
        await vm.AorTab.SavedFiles.LoadAsync(vm.AorTab.SavedFiles.Rows[0]);
        Assert.Equal(AppTab.Aors, vm.ActiveTab);

        vm.ActiveTab = AppTab.Saved;
        await vm.OpsTab.SavedFiles.LoadAsync(vm.OpsTab.SavedFiles.Rows[0]);
        Assert.Equal(AppTab.Ops, vm.ActiveTab);
    }

    [Fact]
    public void SavedTab_TotalsAndLocation()
    {
        var storage = new LocalStorageService(_root);
        var vm = new MainViewModel(storage);
        vm.OpsTab.LoadFromJson(OpsJson, "tot-ops.json");

        Assert.Equal(1, vm.SavedTab.TotalCount);
        Assert.Equal(_root, vm.SavedTab.LocationText);
        Assert.Equal("1 file kept locally", (vm.ActiveTab = AppTab.Saved) == AppTab.Saved ? vm.SectionSubtitle : "");

        vm.OpsTab.SavedFiles.ToggleArchive(vm.OpsTab.SavedFiles.Rows[0]);
        Assert.Equal(0, vm.SavedTab.TotalCount);
    }

    [Fact]
    public void RadiusPresets_ReflectTheCurrentRadius()
    {
        var lookup = new LookupTabViewModel(new OpsTabViewModel());
        Assert.True(lookup.RadiusPresets.Single(p => p.Km == 1).IsSelected);

        lookup.SelectRadiusPreset(5);
        Assert.True(lookup.RadiusPresets.Single(p => p.Km == 5).IsSelected);
        Assert.False(lookup.RadiusPresets.Single(p => p.Km == 1).IsSelected);

        lookup.RadiusKmText = "3.7"; // custom value: no preset highlighted
        Assert.DoesNotContain(lookup.RadiusPresets, p => p.IsSelected);
    }

    [Fact]
    public void OpsStatusFlags_MatchTheStatusText()
    {
        var vm = new MainViewModel(null);
        vm.OpsTab.LoadFromJson(OpsJson, "flag-ops.json");

        var row = vm.OpsTab.FilteredOps[0];

        Assert.Equal(1, new[] { row.IsActiveStatus, row.IsPendingStatus, row.IsExpiredStatus }.Count(b => b));
        Assert.Equal(row.Status, row.StatusLabel.ToLowerInvariant());
    }
}
