using System.Text.Json;
using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public class AorParserTests
{
    // The exact real-world sample AoR data (both schema variants, plus one malformed entry)
    // used earlier in this project's web-app development. Regression fixture.
    private const string SampleJson = """
    [
      {
        "zoneId": "4c6ae8ab-ae77-4b69-9e6a-f44a3004946c",
        "identifier": "LRR8hV2",
        "country": "LTU",
        "name": "EYD16",
        "type": "COMMON",
        "restriction": "PROHIBITED",
        "reason": [],
        "geometry": [
          {
            "uomDimensions": "FT",
            "lowerLimit": 0,
            "lowerVerticalReference": "AGL",
            "upperLimit": 6677,
            "upperVerticalReference": "AGL",
            "horizontalProjection": {
              "type": "Polygon",
              "coordinates": [[[21.15,55.635556],[21.206944,55.640278],[21.252222,55.586111],[21.252222,55.531389],[21.221667,55.523333],[21.15,55.635556]]]
            }
          }
        ],
        "applicability": [{"permanent":"NO","startDateTime":"2026-07-22T07:00:00.000Z","endDateTime":"2026-07-22T06:50:50.813Z","schedule":[]}]
      },
      {
        "identifier": "A428726",
        "country": "LTU",
        "name": "NOTAM A428726",
        "type": "COMMON",
        "restriction": "NO_RESTRICTION",
        "reason": ["OTHER"],
        "message": "DANGER AREA EYD43 RUDNINKAI 5 ACTIVATED",
        "geometry": [
          {
            "uomDimensions": "FT",
            "lowerLimit": 0,
            "lowerVerticalReference": "AGL",
            "upperLimit": 24000,
            "upperVerticalReference": "AGL",
            "horizontalProjection": {"type":"Circle","center":[25.08333,54.36667],"radius":9260.0}
          }
        ],
        "applicability": [{"permanent":"NO","startDateTime":"2026-06-30T06:00:00.000Z","endDateTime":"2026-07-01T06:00:00.000Z","schedule":[]}]
      },
      {
        "identifier": "WEIRD1",
        "country": "LTU",
        "name": "WEIRD ZONE",
        "type": "COMMON",
        "restriction": "NO_RESTRICTION",
        "reason": [],
        "geometry": [
          {"uomDimensions":"FT","lowerLimit":0,"upperLimit":1000,"horizontalProjection":{"type":"Corridor","coordinates":[]}}
        ],
        "applicability": [{"permanent":"NO"}]
      }
    ]
    """;

    [Fact]
    public void ParseAors_ParsesBothSchemas_AndSkipsUnsupportedGeometry()
    {
        var data = JsonDocument.Parse(SampleJson).RootElement;

        var result = AorParser.ParseAors(data);

        Assert.Equal(2, result.Aors.Count); // EYD16 + NOTAM parsed; WEIRD1 (Corridor) skipped
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void ParseAors_CircleZone_MatchesWebAppArea()
    {
        var data = JsonDocument.Parse(SampleJson).RootElement;
        var result = AorParser.ParseAors(data);
        var notam = result.Aors.Single(a => a.Designator == "A428726");
        var parsed = AorParser.ProcessAor(notam);

        Assert.Equal("NOTAM A428726", parsed.Name);
        Assert.Equal("NO_RESTRICTION", parsed.Restriction);
        Assert.Equal("DANGER AREA EYD43 RUDNINKAI 5 ACTIVATED", parsed.Message);
        Assert.Equal(268_951_458, parsed.ComputedArea, tolerance: 1000);
        Assert.Equal("2026-06-30T06:00:00.000Z", parsed.EffectiveTimeBegin);
    }

    [Fact]
    public void ParseAors_PolygonZone_MatchesWebAppArea()
    {
        var data = JsonDocument.Parse(SampleJson).RootElement;
        var result = AorParser.ParseAors(data);
        var eyd16 = result.Aors.Single(a => a.Designator == "LRR8hV2");
        var parsed = AorParser.ProcessAor(eyd16);

        Assert.Equal("EYD16", parsed.Name);
        Assert.Equal("PROHIBITED", parsed.Restriction);
        Assert.Equal(0, parsed.LowerLimit);
        Assert.Equal(6677, parsed.UpperLimit);
        Assert.Equal(45_076_757, parsed.ComputedArea, tolerance: 1000);
    }

    [Fact]
    public void ProcessAor_AssignsDistinctColorsByIndex()
    {
        var data = JsonDocument.Parse(SampleJson).RootElement;
        var result = AorParser.ParseAors(data);

        var colors = result.Aors.Select((a, i) => AorParser.ProcessAor(a, i).Color).ToList();

        Assert.Equal(2, colors.Distinct().Count());
    }

    [Fact]
    public void ParseAors_ThrowsWhenNothingParses()
    {
        var data = JsonDocument.Parse("""[{"not": "an aor"}]""").RootElement;

        Assert.Throws<InvalidDataException>(() => AorParser.ParseAors(data));
    }
}
