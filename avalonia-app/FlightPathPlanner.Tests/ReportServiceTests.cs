using System.Text.Json;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public class ReportServiceTests
{
    // Same op geometry used by OpsParserTests' RealOpsSampleJson (a small polygon near Vilnius).
    private const string OpsSampleJson = """
    {
      "operationPlanId": "a9dfc570-e7ca-11f0-b3a2-6b9a61ffe606",
      "state": "CLOSED",
      "operator": "LTUxdklqpxj9qjws",
      "submitTime": "2026-01-02T11:03:26.869Z",
      "updateTime": "2026-01-02T11:10:51.716Z",
      "closureReason": "NOMINAL",
      "operationVolumes": [
        {
          "alias": "",
          "timeBegin": "2026-01-02T11:08:00.000Z",
          "timeEnd": "2026-01-02T11:13:00.000Z",
          "actualTimeEnd": "2026-01-02T11:10:51.716Z",
          "isBVLOS": false,
          "ordinal": 0,
          "operationGeometry": {
            "minAltitude": {"altitudeValue": 0.0, "altitudeType": "ABOVE_GND", "unitsOfMeasure": "FT"},
            "maxAltitude": {"altitudeValue": 393.7008, "altitudeType": "ABOVE_GND", "unitsOfMeasure": "FT"},
            "geom": {
              "type": "Polygon",
              "coordinates": [[[25.30823116379756,54.603182988989545],[25.308192392337023,54.603411072937476],[25.30621278666149,54.60318300577566],[25.30823116379756,54.603182988989545]]]
            }
          }
        }
      ],
      "contactDetails": {"firstName":"KIPRAS","lastName":"K.","emails":["k@example.com"],"phones":["+37062555542"]},
      "publicInfo": {"title": "Testinis", "description": ""}
    }
    """;

    // A zone-schema AoR polygon that overlaps the op's small polygon above (same Vilnius area).
    private const string OverlappingAorJson = """
    {
      "identifier": "OVERLAP-1",
      "name": "Vilnius Overlap Zone",
      "geometry": [
        {
          "horizontalProjection": {
            "type": "Polygon",
            "coordinates": [[[25.30,54.60],[25.31,54.60],[25.31,54.61],[25.30,54.61],[25.30,54.60]]]
          },
          "lowerLimit": 0,
          "upperLimit": 500,
          "uomDimensions": "FT"
        }
      ]
    }
    """;

    // A zone-schema AoR polygon far from the op — should not intersect.
    private const string DisjointAorJson = """
    {
      "identifier": "DISJOINT-1",
      "name": "Far Away Zone",
      "geometry": [
        {
          "horizontalProjection": {
            "type": "Polygon",
            "coordinates": [[[10.0,50.0],[10.1,50.0],[10.1,50.1],[10.0,50.1],[10.0,50.0]]]
          },
          "lowerLimit": 0,
          "upperLimit": 500,
          "uomDimensions": "FT"
        }
      ]
    }
    """;

    private static (List<Models.ParsedOps> ops, Models.ParsedAor overlapping, Models.ParsedAor disjoint) BuildFixture()
    {
        var opsData = JsonDocument.Parse(OpsSampleJson).RootElement;
        var ops = OpsParser.ParseOps(opsData).Select((o, i) => OpsParser.ProcessOps(o, i)).ToList();

        var overlappingData = JsonDocument.Parse(OverlappingAorJson).RootElement;
        var overlapping = AorParser.ProcessAor(AorParser.ParseAors(overlappingData).Aors[0]);

        var disjointData = JsonDocument.Parse(DisjointAorJson).RootElement;
        var disjoint = AorParser.ProcessAor(AorParser.ParseAors(disjointData).Aors[0]);

        return (ops, overlapping, disjoint);
    }

    [Fact]
    public void GetOpsInAor_ReturnsOps_WhenGeometryIntersects()
    {
        var (ops, overlapping, _) = BuildFixture();

        var matched = ReportService.GetOpsInAor(ops, overlapping);

        Assert.Single(matched);
        Assert.Equal("a9dfc570-e7ca-11f0-b3a2-6b9a61ffe606", matched[0].OperationPlanId);
    }

    [Fact]
    public void GetOpsInAor_ReturnsEmpty_WhenGeometryDoesNotIntersect()
    {
        var (ops, _, disjoint) = BuildFixture();

        var matched = ReportService.GetOpsInAor(ops, disjoint);

        Assert.Empty(matched);
    }

    [Fact]
    public void GetAorReportSummary_CountsMatchesPerAor()
    {
        var (ops, overlapping, disjoint) = BuildFixture();

        var summary = ReportService.GetAorReportSummary(new List<Models.ParsedAor> { overlapping, disjoint }, ops);

        Assert.Equal(2, summary.Count);
        Assert.Equal(1, summary.Single(r => r.Aor.Id == "OVERLAP-1").MatchCount);
        Assert.Equal(0, summary.Single(r => r.Aor.Id == "DISJOINT-1").MatchCount);
    }

    [Fact]
    public void GetOpsNearPoint_ReturnsOps_WithinRadius()
    {
        var (ops, _, _) = BuildFixture();

        // Center right on top of the op's polygon; a small radius should still intersect it.
        var matched = ReportService.GetOpsNearPoint(ops, centerLon: 25.3072, centerLat: 54.6032, radiusKm: 1.0);

        Assert.Single(matched);
    }

    [Fact]
    public void GetOpsNearPoint_ReturnsEmpty_WhenFarFromRadius()
    {
        var (ops, _, _) = BuildFixture();

        var matched = ReportService.GetOpsNearPoint(ops, centerLon: 10.0, centerLat: 50.0, radiusKm: 1.0);

        Assert.Empty(matched);
    }
}
