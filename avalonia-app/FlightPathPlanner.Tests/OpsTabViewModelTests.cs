using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Tests;

public class OpsTabViewModelTests
{
    private const string ThreeOpsJson = """
    [
      {
        "operation_plan_id": "op-1", "title": "Alpha Flight", "operator": "op-alpha",
        "closureReason": "NOMINAL",
        "operation_volumes": [{
          "effective_time_begin": "2026-01-01T10:00:00Z", "effective_time_end": "2026-01-01T11:00:00Z",
          "operation_geography": { "type": "Polygon", "coordinates": [[[0,0],[0,1],[1,1],[1,0],[0,0]]] }
        }]
      },
      {
        "operation_plan_id": "op-2", "title": "Bravo Flight", "operator": "op-bravo",
        "closureReason": "CANCELED",
        "operation_volumes": [{
          "effective_time_begin": "2026-03-01T10:00:00Z", "effective_time_end": "2026-03-01T11:00:00Z",
          "operation_geography": { "type": "Polygon", "coordinates": [[[10,10],[10,11],[11,11],[11,10],[10,10]]] }
        }]
      },
      {
        "operation_plan_id": "op-3", "title": "Charlie Flight", "operator": "op-alpha",
        "closureReason": "CANCELED",
        "operation_volumes": [{
          "effective_time_begin": "2026-06-01T10:00:00Z", "effective_time_end": "2026-06-01T11:00:00Z",
          "operation_geography": { "type": "Polygon", "coordinates": [[[20,20],[20,21],[21,21],[21,20],[20,20]]] }
        }]
      }
    ]
    """;

    private static OpsTabViewModel LoadThreeOps()
    {
        var vm = new OpsTabViewModel();
        vm.LoadFromJson(ThreeOpsJson, "test.json");
        return vm;
    }

    [Fact]
    public void LoadFromJson_PopulatesRowsAndAutoDetectsTimeframe()
    {
        var vm = LoadThreeOps();

        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(3, vm.FilteredCount);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), vm.TimeframeFrom);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero), vm.TimeframeTo);
        Assert.Equal(new[] { "CANCELED", "NOMINAL" }, vm.ClosureReasonChips.Select(c => c.Reason).OrderBy(x => x));
    }

    [Fact]
    public void SearchQuery_FiltersByTitleOperatorAndId()
    {
        var vm = LoadThreeOps();

        vm.SearchQuery = "bravo";
        Assert.Equal(new[] { "op-2" }, vm.FilteredOps.Select(r => r.OperationPlanId));

        vm.SearchQuery = "op-alpha";
        Assert.Equal(new[] { "op-1", "op-3" }, vm.FilteredOps.Select(r => r.OperationPlanId).OrderBy(x => x));

        vm.SearchQuery = "";
        Assert.Equal(3, vm.FilteredCount);
    }

    [Fact]
    public void ClosureReasonChips_FilterAndCombineWithSearch()
    {
        var vm = LoadThreeOps();

        var canceled = vm.ClosureReasonChips.Single(c => c.Reason == "CANCELED");
        canceled.IsSelected = true;

        Assert.Equal(new[] { "op-2", "op-3" }, vm.FilteredOps.Select(r => r.OperationPlanId).OrderBy(x => x));

        vm.ClearClosureReasonsCommand.Execute(null);
        Assert.Equal(3, vm.FilteredCount);
    }

    [Fact]
    public void Timeframe_NarrowsToOverlappingOpsOnly()
    {
        var vm = LoadThreeOps();

        // Narrow to just February (no ops start/end in Feb, but op-1 ends before, op-2 starts after).
        vm.TimeframeFrom = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        vm.TimeframeTo = new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.Empty(vm.FilteredOps);

        // Widen to include March (op-2).
        vm.TimeframeTo = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new[] { "op-2" }, vm.FilteredOps.Select(r => r.OperationPlanId));
    }

    [Fact]
    public void ToggleSelectAll_SelectsThenDeselectsAllVisibleRows()
    {
        var vm = LoadThreeOps();

        vm.ToggleSelectAllCommand.Execute(null);
        Assert.True(vm.AllSelected);
        Assert.Equal(3, vm.SelectedCount);

        vm.ToggleSelectAllCommand.Execute(null);
        Assert.False(vm.AllSelected);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public void DeleteSelected_RemovesOnlyCheckedRowsAndRebuildsClosureChips()
    {
        var vm = LoadThreeOps();
        vm.FilteredOps.Single(r => r.OperationPlanId == "op-1").IsSelected = true;

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal(2, vm.TotalCount);
        Assert.DoesNotContain(vm.FilteredOps, r => r.OperationPlanId == "op-1");
        // NOMINAL was only on op-1, so its chip should be gone now.
        Assert.DoesNotContain(vm.ClosureReasonChips, c => c.Reason == "NOMINAL");
    }

    [Fact]
    public void DeleteOp_RemovesSingleRowAndClearsActiveOpIfItWasSelected()
    {
        var vm = LoadThreeOps();
        var row = vm.FilteredOps.Single(r => r.OperationPlanId == "op-2");
        vm.ActivateOpCommand.Execute(row);
        Assert.NotNull(vm.ActiveOp);

        vm.DeleteOpCommand.Execute(row);

        Assert.Equal(2, vm.TotalCount);
        Assert.Null(vm.ActiveOp);
    }

    [Fact]
    public void ExportSelectedToJson_IncludesOnlySelectedRows()
    {
        var vm = LoadThreeOps();
        vm.FilteredOps.Single(r => r.OperationPlanId == "op-1").IsSelected = true;
        vm.FilteredOps.Single(r => r.OperationPlanId == "op-3").IsSelected = true;

        var json = vm.ExportSelectedToJson();

        Assert.Contains("op-1", json);
        Assert.Contains("op-3", json);
        Assert.DoesNotContain("op-2", json);
        Assert.Contains("\"Total number of ops: 2\"", json);
    }

    [Fact]
    public void ExportSelectedToXlsxBytes_ProducesNonEmptyWorkbook()
    {
        var vm = LoadThreeOps();
        vm.FilteredOps.Single(r => r.OperationPlanId == "op-1").IsSelected = true;

        var bytes = vm.ExportSelectedToXlsxBytes();

        Assert.True(bytes.Length > 0);
        // .xlsx files are zip archives; sanity-check the local file header signature.
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }
}
