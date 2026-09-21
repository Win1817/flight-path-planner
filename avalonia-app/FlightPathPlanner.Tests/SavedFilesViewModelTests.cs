using FlightPathPlanner.Services;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Tests;

public sealed class SavedFilesViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fpp-saved-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string OpsJson = """
    [{"operationPlanId":"OP-1","state":"ACTIVE","operator":"A","closureReason":"NOMINAL","publicInfo":{"title":"One","description":""},
      "operationVolumes":[{"timeBegin":"2026-09-18T08:00:00.000Z","timeEnd":"2026-09-18T09:00:00.000Z","ordinal":0,
      "operationGeometry":{"geom":{"type":"Polygon","coordinates":[[[25.08,54.365],[25.09,54.365],[25.09,54.37],[25.08,54.37],[25.08,54.365]]]}}}]}]
    """;

    [Fact]
    public void UploadingOps_SavesACopy_ThatCanBeReloadedFromThePanel()
    {
        var storage = new LocalStorageService(_root);
        var ops = new OpsTabViewModel(storage);

        Assert.True(ops.LoadFromJson(OpsJson, "flights.json"));

        Assert.Equal(1, ops.SavedFiles.ActiveCount);
        Assert.Equal("flights.json", ops.SavedFiles.Rows[0].DisplayName);

        ops.DeleteSelectedCommand.Execute(null); // nothing selected: no-op
        ops.FilteredOps[0].IsSelected = true;
        ops.DeleteSelectedCommand.Execute(null);
        Assert.False(ops.HasOps);

        ops.SavedFiles.Load(ops.SavedFiles.Rows[0]);

        Assert.True(ops.HasOps);
        Assert.Equal(1, ops.SavedFiles.ActiveCount); // reloading must not save a duplicate
    }

    [Fact]
    public void InvalidUpload_IsNotSaved()
    {
        var ops = new OpsTabViewModel(new LocalStorageService(_root));

        Assert.False(ops.LoadFromJson("not json", "bad.json"));

        Assert.Equal(0, ops.SavedFiles.ActiveCount);
    }

    [Fact]
    public void Archive_MovesRowToArchiveView_AndRestoreBringsItBack()
    {
        var aor = new AorTabViewModel(new LocalStorageService(_root));
        aor.SavedFiles.SaveNew("zones.json", "{}");
        var row = aor.SavedFiles.Rows[0];

        aor.SavedFiles.ToggleArchive(row);

        Assert.Equal(0, aor.SavedFiles.ActiveCount);
        Assert.Equal(1, aor.SavedFiles.ArchivedCount);
        Assert.True(aor.SavedFiles.HasNoRows);

        aor.SavedFiles.ShowArchived = true;
        Assert.Single(aor.SavedFiles.Rows);
        aor.SavedFiles.ToggleArchive(aor.SavedFiles.Rows[0]);

        Assert.Equal(1, aor.SavedFiles.ActiveCount);
        Assert.Equal(0, aor.SavedFiles.ArchivedCount);
    }

    [Fact]
    public void Delete_RequiresConfirmation()
    {
        var saved = new SavedFilesViewModel(new LocalStorageService(_root), StorageCategory.Ops);
        saved.SaveNew("a.json", "{}");
        var row = saved.Rows[0];

        saved.RequestDelete(row);
        Assert.True(row.ConfirmingDelete);
        Assert.Equal(1, saved.ActiveCount);

        saved.RequestDelete(row);
        Assert.Equal(0, saved.ActiveCount);
    }

    [Fact]
    public void BulkArchiveAndDelete_OperateOnSelectedRows_AndDeleteNeedsConfirmation()
    {
        var saved = new SavedFilesViewModel(new LocalStorageService(_root), StorageCategory.Ops);
        saved.SaveNew("a.json", "{}");
        Thread.Sleep(5);
        saved.SaveNew("b.json", "{}");
        Thread.Sleep(5);
        saved.SaveNew("c.json", "{}");
        Assert.Equal(3, saved.ActiveCount);

        saved.Rows[0].IsSelected = true;
        saved.Rows[1].IsSelected = true;
        Assert.Equal(2, saved.SelectedCount);
        saved.ArchiveSelectedCommand.Execute(null);
        Assert.Equal(1, saved.ActiveCount);
        Assert.Equal(2, saved.ArchivedCount);

        saved.ShowArchived = true;
        saved.SelectAllCommand.Execute(null);
        saved.DeleteSelectedCommand.Execute(null);
        Assert.True(saved.ConfirmingBulkDelete);
        Assert.Equal(2, saved.ArchivedCount);
        saved.DeleteSelectedCommand.Execute(null);
        Assert.Equal(0, saved.ArchivedCount);
        Assert.Equal(1, saved.ActiveCount);
    }

    [Fact]
    public void ReportExport_KeepsALocalCopy()
    {
        var storage = new LocalStorageService(_root);
        var vm = new MainViewModel(storage);
        vm.AorTab.LoadFromJson("""{"identifier":"Z1","name":"Zone","geometry":[{"horizontalProjection":{"type":"Polygon","coordinates":[[[25,54],[26,54],[26,55],[25,55],[25,54]]]}}]}""", "aor.json");
        vm.OpsTab.LoadFromJson(OpsJson, "ops.json");

        vm.ReportTab.ExportSummaryToJson();

        var report = Assert.Single(storage.List(StorageCategory.Report, archived: false));
        Assert.StartsWith("geozone-report-", report.DisplayName);
    }

    [Fact]
    public void NoStorage_DisablesPanelWithoutErrors()
    {
        var ops = new OpsTabViewModel();

        Assert.True(ops.LoadFromJson(OpsJson, "x.json"));
        Assert.False(ops.SavedFiles.IsAvailable);
    }
}
