using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public class GeocodeServiceTests
{
    [Theory]
    [InlineData("54.6872, 25.2797")] // comma-separated
    [InlineData("54.6872:25.2797")] // colon-separated
    [InlineData("54.6872 25.2797")] // space-separated
    [InlineData("  54.6872 , 25.2797  ")] // extra whitespace
    [InlineData("-33.8688, 151.2093")] // negative latitude
    public void ParseCoordinates_ParsesAllSupportedSeparators(string query)
    {
        var result = GeocodeService.ParseCoordinates(query);

        Assert.NotNull(result);
    }

    [Fact]
    public void ParseCoordinates_ExtractsCorrectLatLngAndDisplayName()
    {
        var result = GeocodeService.ParseCoordinates("54.6872, 25.2797");

        Assert.NotNull(result);
        Assert.Equal(54.6872, result!.Lat, precision: 4);
        Assert.Equal(25.2797, result.Lng, precision: 4);
        Assert.Equal("54.68720, 25.27970", result.DisplayName);
    }

    [Theory]
    [InlineData("Vilnius, Lithuania")] // free-text address, not coordinates
    [InlineData("91, 25.2797")] // latitude out of range
    [InlineData("54.6872, 200")] // longitude out of range
    [InlineData("not coordinates at all")]
    public void ParseCoordinates_ReturnsNullForNonCoordinateOrOutOfRangeInput(string query)
    {
        var result = GeocodeService.ParseCoordinates(query);

        Assert.Null(result);
    }
}
