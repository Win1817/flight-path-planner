using System.Text.Json;
using FlightPathPlanner.Models;
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

    // Real-world ED-318 sample: plan fields nested under "operationPlan", alongside provider and
    // approval-result metadata this app doesn't use. Same volume shape as ED-269, so only the
    // plan-level unwrap is exercised here.
    private const string Ed318SampleJson = """
    {
      "providerId": "flyk",
      "usspProviderId": "on",
      "operationPlan": {
        "operationPlanId": "d3396010-feaf-11f0-9c3a-e98a2d0c4310",
        "version": "5cc80339-5542-448c-8c6e-f43cafe4271d",
        "state": "CLOSED",
        "operator": "LTUxkpvr9r4n7mzj",
        "submitTime": "2026-01-31T14:19:17.132Z",
        "updateTime": "2026-01-31T14:38:37.483Z",
        "modeOfOperation": "REMOTELY_PILOTED_BVLOS",
        "swarmSize": 1,
        "closureReason": "NOMINAL",
        "operationVolumes": [
          {
            "alias": "",
            "timeBegin": "2026-01-31T14:30:00.000Z",
            "timeEnd": "2026-01-31T14:59:00.000Z",
            "actualTimeEnd": "2026-01-31T14:38:37.483Z",
            "isBVLOS": true,
            "ordinal": 0,
            "operationGeometry": {
              "minAltitude": {"altitudeValue": 0.0, "altitudeType": "ABOVE_GND", "unitsOfMeasure": "FT"},
              "maxAltitude": {"altitudeValue": 393.7008, "altitudeType": "ABOVE_GND", "unitsOfMeasure": "FT"},
              "geom": {
                "type": "Polygon",
                "coordinates": [[[25.295450294958247,54.68697220787487],[25.29541443131262,54.68718274686007],[25.293583333333334,54.68805140665869],[25.29171637170842,54.68697220787487],[25.293583333333334,54.685893037785746],[25.295450294958247,54.68697220787487]]]
              }
            }
          }
        ],
        "contactDetails": {"firstName": "KAROLIS", "lastName": "KUDABA", "emails": ["kavameistris@gmail.com"], "phones": ["+37067488995"]},
        "publicInfo": {"title": "Apžvalginis", "description": ""}
      },
      "localApprovalResults": [{"partialResult": {"state": "GRANTED", "evaluationType": "AUTOMATIC"}}],
      "conflicts": []
    }
    """;

    [Fact]
    public void IsSingleOpsRecord_RecognizesEd318Wrapper()
    {
        var data = JsonDocument.Parse(Ed318SampleJson).RootElement;

        Assert.True(OpsParser.IsSingleOpsRecord(data));
    }

    [Fact]
    public void ParseOps_Ed318Wrapper_UnwrapsOperationPlan()
    {
        var data = JsonDocument.Parse(Ed318SampleJson).RootElement;

        var ops = OpsParser.ParseOps(data);

        Assert.Single(ops);
        var op = ops[0];
        Assert.Equal("d3396010-feaf-11f0-9c3a-e98a2d0c4310", op.OperationPlanId);
        Assert.Equal("Apžvalginis", op.Title); // pulled from operationPlan.publicInfo.title
        Assert.Equal("CLOSED", op.State);
        Assert.Equal("NOMINAL", op.ClosureReason);
        Assert.Equal("LTUxkpvr9r4n7mzj", op.Operator);
        Assert.Equal("REMOTELY_PILOTED_BVLOS", op.ModeOfOperation);
        Assert.Equal(1, op.SwarmSize);
        Assert.Equal("KAROLIS KUDABA", op.Contact?.Name);
        Assert.Equal("kavameistris@gmail.com", op.Contact?.Email);
        Assert.Single(op.OperationVolumes);

        var volume = op.OperationVolumes[0];
        Assert.True(volume.BeyondVisualLineOfSight);
        Assert.Equal("2026-01-31T14:30:00.000Z", volume.EffectiveTimeBegin);
        Assert.Equal(393.7008, volume.MaxAltitude?.AltitudeValue);
        Assert.Equal("ABOVE_GND", volume.MaxAltitude?.VerticalReference);
        Assert.NotNull(volume.OperationGeography); // pulled from operationGeometry.geom, same as ED-269

        var parsed = OpsParser.ProcessOps(op, 0);
        Assert.Equal(1, parsed.ZoneCount);
        Assert.True(parsed.ComputedArea > 0);
    }

    [Fact]
    public void ParseOps_ArrayOfEd318Wrappers_NormalizesEach()
    {
        var data = JsonDocument.Parse($"[{Ed318SampleJson}, {Ed318SampleJson}]").RootElement;

        var ops = OpsParser.ParseOps(data);

        Assert.Equal(2, ops.Count);
        Assert.All(ops, op => Assert.Equal("d3396010-feaf-11f0-9c3a-e98a2d0c4310", op.OperationPlanId));
    }

    [Fact]
    public void ParseOps_Ed318_TagsSchemaAndCapturesApprovalClearanceConflicts()
    {
        // localApprovalResults/localTakeoffClearanceResults are version histories: the entry with the latest
        // updateTime wins, not the first or last array element. Deliberately out of order here to prove that.
        const string json = """
        {
          "providerId": "flyk",
          "operationPlan": { "operationPlanId": "op-1", "operationVolumes": [] },
          "localApprovalResults": [
            {"updateTime": "2026-01-31T14:19:17.383Z", "partialResult": {"state": "GRANTED", "evaluationType": "AUTOMATIC"}},
            {"updateTime": "2026-01-31T10:00:00.000Z", "partialResult": {"state": "PENDING", "evaluationType": "AUTOMATIC"}}
          ],
          "localTakeoffClearanceResults": [
            {"updateTime": "2026-01-31T14:23:09.480Z", "partialResult": {"state": "DENIED", "evaluationType": "AUTOMATIC"}},
            {"updateTime": "2026-01-31T14:23:09.515Z", "partialResult": {"state": "GRANTED", "evaluationType": "AUTOMATIC"}}
          ],
          "conflicts": [
            {"message": "Resolved conflict", "conflictType": "TEXTUAL_RESTRICTION", "resolved": true, "rejecting": false},
            {"message": "Unresolved conflict", "conflictType": "AUTHORITY_REQUIREMENTS", "resolved": false, "rejecting": false}
          ]
        }
        """;

        var data = JsonDocument.Parse(json).RootElement;
        var op = OpsParser.ParseOps(data)[0];

        Assert.Equal(OpsSchema.Ed318, op.Schema);
        Assert.Equal("flyk", op.ProviderId);
        Assert.Equal("GRANTED", op.Approval?.State); // the later of the two approval entries
        Assert.Equal("GRANTED", op.TakeoffClearance?.State); // the later of the two clearance entries
        Assert.Equal(2, op.Conflicts.Count);
        Assert.Single(op.Conflicts, c => !c.Resolved);
        Assert.Contains(op.Conflicts, c => c is { Resolved: false, Message: "Unresolved conflict" });
    }

    [Fact]
    public void ParseOps_Ed269_LeavesEd318OnlyFieldsEmpty()
    {
        var data = JsonDocument.Parse(RealOpsSampleJson).RootElement;
        var op = OpsParser.ParseOps(data)[0];

        Assert.Equal(OpsSchema.Ed269, op.Schema);
        Assert.Null(op.ProviderId);
        Assert.Null(op.Approval);
        Assert.Null(op.TakeoffClearance);
        Assert.Empty(op.Conflicts);
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
