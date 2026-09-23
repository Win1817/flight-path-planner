using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Tests;

public class ReportTabViewModelTests
{
    // op-1 sits inside the AoR polygon below; op-2 is far away and never matches.
    private const string TwoOpsJson = """
    [
      {
        "operation_plan_id": "op-1", "title": "Inside Flight", "operator": "op-alpha",
        "operation_volumes": [{
          "effective_time_begin": "2026-01-01T10:00:00Z", "effective_time_end": "2026-01-01T11:00:00Z",
          "operation_geography": { "type": "Polygon", "coordinates": [[[0.2,0.2],[0.2,0.4],[0.4,0.4],[0.4,0.2],[0.2,0.2]]] }
        }]
      },
      {
        "operation_plan_id": "op-2", "title": "Outside Flight", "operator": "op-bravo",
        "operation_volumes": [{
          "effective_time_begin": "2026-01-01T10:00:00Z", "effective_time_end": "2026-01-01T11:00:00Z",
          "operation_geography": { "type": "Polygon", "coordinates": [[[10,10],[10,11],[11,11],[11,10],[10,10]]] }
        }]
      }
    ]
    """;

    private const string OneAorJson = """
    [
      {
        "identifier": "AOR-1", "name": "Test Zone",
        "geometry": [
          { "uomDimensions": "FT", "lowerLimit": 0, "upperLimit": 1000,
            "horizontalProjection": { "type": "Polygon", "coordinates": [[[0,0],[0,1],[1,1],[1,0],[0,0]]] } }
        ]
      }
    ]
    """;

    private static (ReportTabViewModel report, OpsTabViewModel ops) BuildWithOneMatch()
    {
        var ops = new OpsTabViewModel();
        ops.LoadFromJson(TwoOpsJson, "ops.json", persist: false);
        var aors = new AorTabViewModel();
        aors.LoadFromJson(OneAorJson, "aor.json", persist: false);
        var report = new ReportTabViewModel(ops, aors);
        report.SelectedAor = report.AorOptions[0];
        return (report, ops);
    }

    [Fact]
    public void SelectingAor_PopulatesOnlyTheIntersectingOp()
    {
        var (report, _) = BuildWithOneMatch();

        Assert.Equal(1, report.MatchCount);
        Assert.Equal("op-1", report.MatchingOps[0].OperationPlanId);
    }

    [Fact]
    public void TappingAMatchedRow_TogglesItsDetailsOpenThenClosed()
    {
        var (report, _) = BuildWithOneMatch();
        var row = report.MatchingOps[0];

        report.ActivateOpCommand.Execute(row);
        Assert.Same(row, report.ActiveOp);

        report.ActivateOpCommand.Execute(row); // same row again -> closes
        Assert.Null(report.ActiveOp);
    }

    [Fact]
    public void CloseDetailsCommand_ClearsActiveOp()
    {
        var (report, _) = BuildWithOneMatch();
        report.ActivateOpCommand.Execute(report.MatchingOps[0]);

        report.CloseDetailsCommand.Execute(null);

        Assert.Null(report.ActiveOp);
    }

    [Fact]
    public void SwitchingSelectedAor_ClosesAnyOpenDetails()
    {
        var (report, _) = BuildWithOneMatch();
        report.ActivateOpCommand.Execute(report.MatchingOps[0]);
        Assert.NotNull(report.ActiveOp);

        report.SelectedAor = null;

        Assert.Null(report.ActiveOp);
    }

    [Fact]
    public void NarrowingOpsFilter_ClosesDetails_WhenTheActiveOpFallsOutOfTheMatchSet()
    {
        var (report, ops) = BuildWithOneMatch();
        report.ActivateOpCommand.Execute(report.MatchingOps[0]); // op-1
        Assert.NotNull(report.ActiveOp);

        ops.SearchQuery = "Outside"; // op-1 no longer passes the OPS tab's own filter

        Assert.Equal(0, report.MatchCount);
        Assert.Null(report.ActiveOp);
    }
}
