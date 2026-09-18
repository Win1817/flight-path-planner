using System;

namespace FlightPathPlanner.Services;

/// <summary>
/// Geodesic math ported verbatim from the web app's @turf/turf usage, so
/// areas and search-radius circles produced here match the web app exactly.
/// Coordinates follow GeoJSON convention: [longitude, latitude].
///
/// Ported from (all under flight-path-planner/node_modules/@turf/):
///   - @turf/helpers:    earthRadius constant
///   - @turf/area:       ringArea / polygonArea / calculateArea
///   - @turf/destination: destination()
///   - @turf/circle:     circle()
/// Do not "simplify" these formulas — they must match turf's output bit for
/// bit closely enough that area/circle values agree with the existing web app.
/// </summary>
public static class GeoMath
{
    public const double EarthRadiusMeters = 6371008.8;

    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    /// <summary>Signed area of a single ring (array of [lon, lat] points, first == last), in m^2.</summary>
    public static double RingArea(double[][] coords)
    {
        int coordsLength = coords.Length - 1;
        if (coordsLength <= 2) return 0;

        double total = 0;
        double factor = EarthRadiusMeters * EarthRadiusMeters / 2.0;

        for (int i = 0; i < coordsLength; i++)
        {
            double[] lower = coords[i];
            double[] middle = coords[i + 1 == coordsLength ? 0 : i + 1];
            double[] upper = coords[i + 2 >= coordsLength ? (i + 2) % coordsLength : i + 2];

            double lowerX = lower[0] * DegToRad;
            double middleY = middle[1] * DegToRad;
            double upperX = upper[0] * DegToRad;

            total += (upperX - lowerX) * Math.Sin(middleY);
        }

        return total * factor;
    }

    /// <summary>Area of a Polygon (outer ring minus holes), in m^2. coords: rings -> points -> [lon, lat].</summary>
    public static double PolygonArea(double[][][] coords)
    {
        if (coords.Length == 0) return 0;

        double total = Math.Abs(RingArea(coords[0]));
        for (int i = 1; i < coords.Length; i++)
        {
            total -= Math.Abs(RingArea(coords[i]));
        }
        return total;
    }

    /// <summary>Area of a MultiPolygon, in m^2. coords: polygons -> rings -> points -> [lon, lat].</summary>
    public static double MultiPolygonArea(double[][][][] coords)
    {
        double total = 0;
        foreach (var polygon in coords)
        {
            total += PolygonArea(polygon);
        }
        return total;
    }

    /// <summary>
    /// Great-circle destination point, distanceMeters from [lon, lat] at the given bearing (degrees).
    /// Returns [lon, lat].
    /// </summary>
    public static double[] Destination(double lon, double lat, double distanceMeters, double bearingDegrees)
    {
        double lon1 = lon * DegToRad;
        double lat1 = lat * DegToRad;
        double bearingRad = bearingDegrees * DegToRad;
        double radians = distanceMeters / EarthRadiusMeters;

        double lat2 = Math.Asin(
            Math.Sin(lat1) * Math.Cos(radians) +
            Math.Cos(lat1) * Math.Sin(radians) * Math.Cos(bearingRad));

        double lon2 = lon1 + Math.Atan2(
            Math.Sin(bearingRad) * Math.Sin(radians) * Math.Cos(lat1),
            Math.Cos(radians) - Math.Sin(lat1) * Math.Sin(lat2));

        return new[] { lon2 * RadToDeg, lat2 * RadToDeg };
    }

    /// <summary>
    /// Builds a circle polygon (single ring, closed) centered at [lon, lat] with the given
    /// radius in kilometers, matching turf.circle's default of 64 steps.
    /// Returns Polygon coordinates: rings -> points -> [lon, lat] (one ring).
    /// </summary>
    public static double[][][] Circle(double lon, double lat, double radiusKm, int steps = 64)
    {
        double radiusMeters = radiusKm * 1000.0;
        var ring = new double[steps + 1][];
        for (int i = 0; i < steps; i++)
        {
            double bearing = i * -360.0 / steps;
            ring[i] = Destination(lon, lat, radiusMeters, bearing);
        }
        ring[steps] = ring[0];
        return new[] { ring };
    }

    /// <summary>Great-circle (haversine) distance between two [lon, lat] points, in kilometers.
    /// Ported from @turf/distance, using the same earthRadius constant (converted to km).</summary>
    public static double DistanceKm(double lon1, double lat1, double lon2, double lat2)
    {
        double dLat = (lat2 - lat1) * DegToRad;
        double dLon = (lon2 - lon1) * DegToRad;
        double lat1Rad = lat1 * DegToRad;
        double lat2Rad = lat2 * DegToRad;

        double a = Math.Pow(Math.Sin(dLat / 2), 2) +
                   Math.Pow(Math.Sin(dLon / 2), 2) * Math.Cos(lat1Rad) * Math.Cos(lat2Rad);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return (EarthRadiusMeters / 1000.0) * c;
    }

    /// <summary>Arithmetic mean of every vertex coordinate across all rings of a MultiPolygon —
    /// matches @turf/centroid, which is a simple vertex mean, not an area-weighted centroid.
    /// Returns [lon, lat].</summary>
    public static double[] Centroid(double[][][][] polygons)
    {
        double xSum = 0, ySum = 0;
        int count = 0;
        foreach (var polygon in polygons)
        foreach (var ring in polygon)
        foreach (var point in ring)
        {
            xSum += point[0];
            ySum += point[1];
            count++;
        }
        return count == 0 ? new double[] { 0, 0 } : new[] { xSum / count, ySum / count };
    }
}
