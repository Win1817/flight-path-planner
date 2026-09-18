using System.Text.Json;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public class OpsParserTests
{
    [Theory]
    [InlineData("2026-01-02T11:08:00.000Z", "2026-01-02T11:08:00.000Z")] // already has a bare "Z" -> must NOT get a second Z appended
    [InlineData("2026-01-13T09:00:00Z", "2026-01-13T09:00:00Z")]
    [InlineData("2026-01-13T09:00:00+05:00", "2026-01-13T09:00:00+05:00")]
    [InlineData("2026-01-13T09:00:00-0800", "2026-01-13T09:00:00-0800")]
    [InlineData("2026-01-13T09:00:00", "2026-01-13T09:00:00Z")] // no timezone marker -> assume UTC
    public void EnsureUtc_HandlesAllTimezoneFormats(string input, string expected)
    {
        var result = OpsParser.EnsureUtc(input);

        Assert.Equal(expected, result);
        Assert.True(DateTimeOffset.TryParse(result, out _), $"'{result}' should be a parseable date");
    }

    // The exact real-world OPS sample (camelCase fields, nested operationGeometry.geom) that
    // originally exposed the ensureUtc bug earlier in this project's development.
    private const string RealOpsSampleJson = """
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

    [Fact]
    public void ParseOps_SingleObject_NormalizesCamelCaseAndNestedGeometry()
    {
        var data = JsonDocument.Parse(RealOpsSampleJson).RootElement;

        var ops = OpsParser.ParseOps(data);

        Assert.Single(ops);
        var op = ops[0];
        Assert.Equal("a9dfc570-e7ca-11f0-b3a2-6b9a61ffe606", op.OperationPlanId);
        Assert.Equal("Testinis", op.Title); // pulled from publicInfo.title
        Assert.Equal("NOMINAL", op.ClosureReason);
        Assert.Equal("KIPRAS K.", op.Contact?.Name);
        Assert.Equal("+37062555542", op.Contact?.Phone);
        Assert.Single(op.OperationVolumes);

        var volume = op.OperationVolumes[0];
        Assert.NotNull(volume.OperationGeography); // pulled from operationGeometry.geom
        Assert.Equal(393.7008, volume.MaxAltitude?.AltitudeValue);
        Assert.Equal("2026-01-02T11:08:00.000Z", volume.EffectiveTimeBegin);
    }

    [Fact]
    public void ProcessOps_DoesNotFallBackToNow_WhenTimestampsAreValid()
    {
        // Regression test for the real bug this caught: a broken ensureUtc regex silently
        // corrupted every "...Z"-suffixed timestamp, which made getVolumeTimeRange fall back to
        // `new Date()` (today) instead of the real operation date, breaking the timeframe filter.
        var data = JsonDocument.Parse(RealOpsSampleJson).RootElement;
        var ops = OpsParser.ParseOps(data);
        var parsed = OpsParser.ProcessOps(ops[0], 0);

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 11, 8, 0, TimeSpan.Zero), parsed.StartTime);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 11, 13, 0, TimeSpan.Zero), parsed.EndTime);
        Assert.NotEqual(DateTimeOffset.UtcNow.Date, parsed.StartTime.Date); // sanity: not "today"
        Assert.Equal(1, parsed.ZoneCount);
        Assert.True(parsed.ComputedArea > 0);
        Assert.Equal(OpsParser.ZoneColors[0], parsed.Color);
    }

    [Fact]
    public void ParseOps_WrapperShapes_AreAllRecognized()
    {
        AssertParsesOneOp("""{"plans": [{"operation_plan_id": "p1", "operation_volumes": []}]}""");
        AssertParsesOneOp("""{"operations": [{"operation_plan_id": "p1", "operation_volumes": []}]}""");
        AssertParsesOneOp("""{"flight_plans": [{"operation_plan_id": "p1", "operation_volumes": []}]}""");
        AssertParsesOneOp("""[{"operation_plan_id": "p1", "operation_volumes": []}]""");

        static void AssertParsesOneOp(string json)
        {
            var data = JsonDocument.Parse(json).RootElement;
            var ops = OpsParser.ParseOps(data);
            Assert.Single(ops);
        }
    }

    [Fact]
    public void ParseOps_ThrowsOnUnrecognizedShape()
    {
        var data = JsonDocument.Parse("""{"nothing": "useful"}""").RootElement;

        Assert.Throws<InvalidDataException>(() => OpsParser.ParseOps(data));
    }

    [Theory]
    [InlineData(5_000, "5000 m²")]
    [InlineData(15_000, "1.50 ha")]
    [InlineData(2_500_000, "2.50 km²")]
    public void FormatArea_MatchesWebAppThresholds(double squareMeters, string expected)
    {
        Assert.Equal(expected, OpsParser.FormatArea(squareMeters));
    }
}
