using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public class GeoMathTests
{
    // Regression fixtures: these exact area values (in m^2) were produced by
    // the real web app's @turf/turf pipeline against real sample AoR data
    // earlier in this project's development. The ported GeoMath must
    // reproduce them (within floating point tolerance) or area/search-radius
    // results will silently disagree with the web app.

    [Fact]
    public void Circle_MatchesWebAppAreaForNotamZone()
    {
        // NOTAM A428726: Circle horizontalProjection, center [25.08333, 54.36667], radius 9260m.
        var ring = GeoMath.Circle(25.08333, 54.36667, radiusKm: 9.26);
        var polygon = new[] { ring[0] };

        double area = GeoMath.PolygonArea(polygon);

        Assert.Equal(268_951_458, area, tolerance: 1000); // within 1000 m^2 of the web app's value
    }

    [Fact]
    public void PolygonArea_MatchesWebAppAreaForEyd16Zone()
    {
        // EYD16: real Polygon horizontalProjection from the sample AoR data.
        double[][] ring =
        {
            new[] { 21.15, 55.635556 },
            new[] { 21.206944, 55.640278 },
            new[] { 21.252222, 55.586111 },
            new[] { 21.252222, 55.531389 },
            new[] { 21.221667, 55.523333 },
            new[] { 21.15, 55.635556 },
        };

        double area = GeoMath.PolygonArea(new[] { ring });

        Assert.Equal(45_076_757, area, tolerance: 1000);
    }

    [Fact]
    public void Circle_IsClosedRingWithRequestedStepCount()
    {
        var ring = GeoMath.Circle(0, 0, radiusKm: 1, steps: 64)[0];

        Assert.Equal(65, ring.Length); // 64 steps + closing point
        Assert.Equal(ring[0][0], ring[^1][0], precision: 10);
        Assert.Equal(ring[0][1], ring[^1][1], precision: 10);
    }

    [Fact]
    public void Destination_NorthBearingIncreasesLatitude()
    {
        var result = GeoMath.Destination(0, 0, distanceMeters: 111_195, bearingDegrees: 0); // ~1 degree of latitude

        Assert.Equal(0, result[0], precision: 3); // longitude unchanged heading due north
        Assert.True(result[1] > 0.99 && result[1] < 1.01);
    }

    [Fact]
    public void MultiPolygonArea_SumsEachPolygon()
    {
        double[][] square1 =
        {
            new[] { 0.0, 0.0 }, new[] { 0.0, 1.0 }, new[] { 1.0, 1.0 }, new[] { 1.0, 0.0 }, new[] { 0.0, 0.0 },
        };
        double[][] square2 =
        {
            new[] { 10.0, 10.0 }, new[] { 10.0, 11.0 }, new[] { 11.0, 11.0 }, new[] { 11.0, 10.0 }, new[] { 10.0, 10.0 },
        };

        double multi = GeoMath.MultiPolygonArea(new[] { new[] { square1 }, new[] { square2 } });
        double sumOfIndividual = GeoMath.PolygonArea(new[] { square1 }) + GeoMath.PolygonArea(new[] { square2 });

        Assert.Equal(sumOfIndividual, multi, precision: 6);
    }

    [Fact]
    public void DistanceKm_ZeroForSamePoint()
    {
        Assert.Equal(0, GeoMath.DistanceKm(25.0, 54.0, 25.0, 54.0), precision: 6);
    }

    [Fact]
    public void DistanceKm_MatchesKnownOneDegreeLatitudeSeparation()
    {
        // ~1 degree of latitude is ~111.19 km at the equator (same constant used by turf/GeoMath's
        // Destination test above: 111,195m for 1 degree of northward travel).
        double km = GeoMath.DistanceKm(0, 0, 0, 1);

        Assert.True(km > 111.0 && km < 111.4);
    }

    [Fact]
    public void Centroid_IsArithmeticMeanOfAllVertices_NotAreaWeighted()
    {
        // A single square ring: mean of its 5 vertices (closing point repeats [0,0]).
        double[][][][] polygons =
        {
            new[]
            {
                new[]
                {
                    new[] { 0.0, 0.0 }, new[] { 0.0, 2.0 }, new[] { 2.0, 2.0 }, new[] { 2.0, 0.0 }, new[] { 0.0, 0.0 },
                },
            },
        };

        var centroid = GeoMath.Centroid(polygons);

        Assert.Equal(0.8, centroid[0], precision: 6); // (0+0+2+2+0)/5
        Assert.Equal(0.8, centroid[1], precision: 6); // (0+2+2+0+0)/5
    }
}
