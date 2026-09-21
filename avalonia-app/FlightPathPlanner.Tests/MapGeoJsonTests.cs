using System.Text.Json;
using FlightPathPlanner.Services;
using FlightPathPlanner.ViewModels;

namespace FlightPathPlanner.Tests;

public class MapGeoJsonTests
{
    private const string OpsJson = """
    [
      {"operationPlanId":"OP-1","state":"ACTIVE","operator":"A","closureReason":"NOMINAL",
       "publicInfo":{"title":"Inside","description":""},
       "operationVolumes":[{"timeBegin":"2026-09-18T08:00:00.000Z","timeEnd":"2026-09-18T09:00:00.000Z","ordinal":0,
         "operationGeometry":{"maxAltitude":{"altitudeValue":400,"unitsOfMeasure":"FT"},
           "geom":{"type":"Polygon","coordinates":[[[25.08,54.365],[25.09,54.365],[25.09,54.37],[25.08,54.37],[25.08,54.365]]]}}}]},
      {"operationPlanId":"OP-2","state":"ACTIVE","operator":"B","closureReason":"NOMINAL",
       "publicInfo":{"title":"Far","description":""},
       "operationVolumes":[{"timeBegin":"2026-09-18T10:00:00.000Z","timeEnd":"2026-09-18T11:00:00.000Z","ordinal":0,
         "operationGeometry":{"geom":{"type":"Polygon","coordinates":[[[21.1,55.7],[21.11,55.7],[21.11,55.71],[21.1,55.71],[21.1,55.7]]]}}}]}
    ]
    """;

    private const string AorJson = """
    {"identifier":"Z1","name":"Zone One","restriction":"PROHIBITED",
     "geometry":[{"horizontalProjection":{"type":"Circle","center":[25.08333,54.36667],"radius":9260},"lowerLimit":0,"upperLimit":500,"uomDimensions":"FT"}]}
    """;

    private static MainViewModel LoadedViewModel()
    {
        var vm = new MainViewModel(null);
        vm.OpsTab.LoadFromJson(OpsJson, "ops.json");
        vm.AorTab.LoadFromJson(AorJson, "aor.json");
        return vm;
    }

    private static JsonElement[] Features(string geojson) =>
        JsonDocument.Parse(geojson).RootElement.GetProperty("features").EnumerateArray().Select(f => f.Clone()).ToArray();

    [Fact]
    public void OpsTab_MapContainsOneFeaturePerVolumeWithExpectedProperties()
    {
        var vm = LoadedViewModel();

        var features = Features(vm.BuildMapGeoJson());

        Assert.Equal(2, features.Length);
        var props = features[0].GetProperty("properties");
        Assert.Equal("OP-1", props.GetProperty("opsId").GetString());
        Assert.Equal("ops", props.GetProperty("dataType").GetString());
        Assert.Equal("Inside", props.GetProperty("title").GetString());
        Assert.Equal(400, props.GetProperty("maxAltitude").GetDouble());
        Assert.True(props.GetProperty("area").GetDouble() > 0);
        Assert.Equal("Polygon", features[0].GetProperty("geometry").GetProperty("type").GetString());
    }

    [Fact]
    public void AorTab_MapContainsAorFeaturesOnly()
    {
        var vm = LoadedViewModel();
        vm.ActiveTab = AppTab.Aors;

        var features = Features(vm.BuildMapGeoJson());

        Assert.Single(features);
        Assert.Equal("Z1", features[0].GetProperty("properties").GetProperty("aorId").GetString());
    }

    [Fact]
    public void ReportTab_WithSelectedAor_ShowsAorPlusIntersectingOpsOnly()
    {
        var vm = LoadedViewModel();
        vm.ActiveTab = AppTab.Report;
        vm.ReportTab.SelectedAor = vm.ReportTab.AorOptions[0];

        var features = Features(vm.BuildMapGeoJson());

        Assert.Equal(2, features.Length); // the AoR + OP-1 (OP-2 is far away)
        Assert.Contains("OP-1", vm.BuildHighlightIds());
        Assert.DoesNotContain("OP-2", vm.BuildHighlightIds());
    }

    [Fact]
    public void LookupTab_ShowsRadiusCircleAndNearbyOps()
    {
        var vm = LoadedViewModel();
        vm.ActiveTab = AppTab.Lookup;
        vm.LookupTab.SelectCandidate(new GeocodeResult(54.365, 25.08, "here"));

        var features = Features(vm.BuildMapGeoJson());

        Assert.Equal(2, features.Length); // radius circle + OP-1
        Assert.Equal(MapGeoJson.LookupRadiusId, features[0].GetProperty("properties").GetProperty("aorId").GetString());
    }

    [Fact]
    public void SelectingOpsAndHoveringAreHighlighted()
    {
        var vm = LoadedViewModel();
        vm.OpsTab.FilteredOps[1].IsSelected = true;
        vm.HandleMapZoneHovered("OP-1");

        var ids = vm.BuildHighlightIds();

        Assert.Contains("OP-2", ids);
        Assert.Contains("OP-1", ids);
    }

    [Fact]
    public void ClickingOpOnMap_ActivatesItAndSwitchesAwayFromAorsTab()
    {
        var vm = LoadedViewModel();
        vm.ActiveTab = AppTab.Aors;

        vm.HandleMapZoneClicked("OP-2", "ops");

        Assert.Equal("OP-2", vm.OpsTab.ActiveOp?.OperationPlanId);
        Assert.Equal(AppTab.Ops, vm.ActiveTab);
    }

    [Fact]
    public void ChangingTabOrFiltersRaisesMapInvalidation()
    {
        var vm = LoadedViewModel();
        int dataEvents = 0;
        vm.MapDataInvalidated += () => dataEvents++;

        vm.ActiveTab = AppTab.Aors;
        vm.ActiveTab = AppTab.Ops;
        vm.OpsTab.SearchQuery = "Far";

        Assert.True(dataEvents >= 3);
        Assert.Single(Features(vm.BuildMapGeoJson()));
    }
}
