using System.Globalization;
using System.Text;

namespace FlightPathPlanner.Tests.TestData;

/// <summary>Builds synthetic GeoZone/AoR files that follow the real "airspace zone" schema seen in production data
/// (zoneId, identifier, name, restriction, reason[], message, geometry[] with Polygon/Circle projections, applicability[]).
/// Geometry sizes are deliberately skewed like real airspace: most zones are small, a few national boundaries have thousands of
/// vertices. Output is deterministic for a given seed.</summary>
public static class GeoZoneDatasetGenerator
{
    private static readonly string[] Restrictions = { "PROHIBITED", "REQ_AUTHORISATION", "CONDITIONAL", "NO_RESTRICTION" };
    private static readonly string[] Reasons = { "AIR_TRAFFIC", "SENSITIVE", "PRIVACY", "POPULATION", "NATURE", "OTHER" };

    public static string Generate(int zones, int seed = 42, bool wrapInObject = false, int malformedEvery = 0)
    {
        var sb = new StringBuilder(zones * 900);
        WriteTo(sb, zones, seed, wrapInObject, malformedEvery);
        return sb.ToString();
    }

    public static void WriteToFile(string path, int zones, int seed = 42, bool wrapInObject = false, int malformedEvery = 0)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
        var sb = new StringBuilder(1 << 16);
        writer.Write(wrapInObject ? "{\"zones\":[" : "[");
        for (int i = 0; i < zones; i++)
        {
            if (i > 0) writer.Write(',');
            sb.Clear();
            WriteZone(sb, i, seed, malformedEvery);
            writer.Write(sb.ToString());
        }
        writer.Write(wrapInObject ? "]}" : "]");
    }

    private static void WriteTo(StringBuilder sb, int zones, int seed, bool wrapInObject, int malformedEvery)
    {
        sb.Append(wrapInObject ? "{\"zones\":[" : "[");
        for (int i = 0; i < zones; i++)
        {
            if (i > 0) sb.Append(',');
            WriteZone(sb, i, seed, malformedEvery);
        }
        sb.Append(wrapInObject ? "]}" : "]");
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static void WriteZone(StringBuilder sb, int index, int seed, int malformedEvery)
    {
        var rng = new Random(seed * 1_000_003 + index);

        if (malformedEvery > 0 && index % malformedEvery == malformedEvery - 1)
        {
            // Looks like a zone but has an unusable geometry (unsupported projection type) -> must be skipped, not fatal.
            sb.Append("{\"identifier\":\"BAD").Append(index).Append("\",\"name\":\"Broken ").Append(index)
              .Append("\",\"geometry\":[{\"horizontalProjection\":{\"type\":\"Corridor\",\"coordinates\":[]},\"lowerLimit\":0,\"upperLimit\":100}]}");
            return;
        }

        // Cluster centres across a Europe-sized box so the spatial index has realistic, uneven density.
        double cx = 10 + rng.NextDouble() * 20 + Math.Sin(index * 0.013) * 3;
        double cy = 47 + rng.NextDouble() * 13 + Math.Cos(index * 0.017) * 2;

        string restriction = Restrictions[rng.Next(Restrictions.Length)];
        sb.Append("{\"zoneId\":\"").Append(Guid.NewGuid().ToString("N")[..8]).Append('-').Append(index)
          .Append("\",\"identifier\":\"ZN").Append(index.ToString("D6"))
          .Append("\",\"country\":\"EST\",\"name\":\"").Append(index % 7 == 0 ? "NOTAM " : "EEGZ").Append(index)
          .Append(" Zone ").Append(index % 13 == 0 ? "Helipad" : "Area").Append(" Sector ").Append(rng.Next(1, 99))
          .Append("\",\"type\":\"COMMON\",\"restriction\":\"").Append(restriction).Append("\",\"reason\":[\"")
          .Append(Reasons[rng.Next(Reasons.Length)]).Append("\"],");
        if (index % 3 == 0)
            sb.Append("\"message\":\"Temporary restriction ").Append(index).Append(" due to activity, see AIP supplement for details of the affected area and times.\",");

        sb.Append("\"geometry\":[{\"uomDimensions\":\"FT\",\"lowerLimit\":0,\"lowerVerticalReference\":\"AGL\",\"upperLimit\":")
          .Append(rng.Next(1, 240) * 100).Append(",\"upperVerticalReference\":\"AGL\",\"horizontalProjection\":");

        int kind = rng.Next(100);
        if (kind < 20)
        {
            sb.Append("{\"type\":\"Circle\",\"center\":[").Append(F(cx)).Append(',').Append(F(cy)).Append("],\"radius\":")
              .Append(rng.Next(300, 20000)).Append(".0}");
        }
        else
        {
            int points = kind < 90 ? rng.Next(8, 60) : kind < 99 ? rng.Next(100, 400) : rng.Next(2000, 5000);
            double radius = points > 1000 ? 1.5 : 0.02 + rng.NextDouble() * 0.15;
            sb.Append("{\"type\":\"Polygon\",\"coordinates\":[[");
            double first0 = 0, first1 = 0;
            for (int p = 0; p < points; p++)
            {
                double angle = 2 * Math.PI * p / points;
                double r = radius * (0.7 + 0.3 * Math.Sin(p * 0.9 + index));
                double x = cx + r * Math.Cos(angle) * 1.6;
                double y = cy + r * Math.Sin(angle);
                if (p == 0) { first0 = x; first1 = y; } else sb.Append(',');
                sb.Append('[').Append(F(x)).Append(',').Append(F(y)).Append(']');
            }
            sb.Append(",[").Append(F(first0)).Append(',').Append(F(first1)).Append("]]]}");
        }

        sb.Append("}],\"applicability\":[{\"permanent\":\"NO\",\"startDateTime\":\"2026-0")
          .Append(1 + index % 9).Append("-1").Append(index % 9).Append("T06:00:00.000Z\",\"endDateTime\":\"2026-1")
          .Append(index % 3).Append("-2").Append(index % 8).Append("T18:00:00.000Z\",\"schedule\":[]}]}");
    }
}
