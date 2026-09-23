using System.Globalization;
using System.Text;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;

namespace FlightPathPlanner.Tests;

/// <summary>A file reaches hundreds of MB through vertex-heavy shapes as easily as through record count, so a viewport cap on
/// feature count alone doesn't bound what the map page must parse and tessellate. These pin the vertex-side limits.</summary>
public class MapDetailTests
{
    private static double[][] Circle(int points, double radius, double cx = 10, double cy = 50)
    {
        var ring = new double[points + 1][];
        for (int i = 0; i < points; i++)
        {
            double a = 2 * Math.PI * i / points;
            ring[i] = new[] { cx + radius * Math.Cos(a), cy + radius * Math.Sin(a) };
        }
        ring[points] = ring[0];
        return ring;
    }

    private static double Area(double[][] ring)
    {
        double sum = 0;
        for (int i = 0; i < ring.Length - 1; i++) sum += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1];
        return Math.Abs(sum) / 2;
    }

    [Fact]
    public void SimplifyRing_CutsVerticesButKeepsShapeAndStaysClosed()
    {
        var ring = Circle(1000, 1.0);

        var simplified = MapGeoJson.SimplifyRing(ring, 0.01);

        Assert.InRange(simplified.Length, 10, 100);
        Assert.Equal(simplified[0], simplified[^1]);
        Assert.InRange(Area(simplified) / Area(ring), 0.98, 1.0); // a polygon inscribed in the circle, within tolerance
    }

    [Fact]
    public void SimplifyRing_ShapeSmallerThanTolerance_StillYieldsAValidClosedRing()
    {
        var tiny = Circle(500, 0.0001);

        var simplified = MapGeoJson.SimplifyRing(tiny, 0.05);

        Assert.True(simplified.Length >= 4);
        Assert.Equal(simplified[0], simplified[^1]);
    }

    [Fact]
    public void SimplifyRing_ZeroDetail_IsNotUsedByCallers_ButShortRingsAreLeftAlone()
    {
        var ring = Circle(6, 1.0);

        Assert.Same(ring, MapGeoJson.SimplifyRing(ring, 0.5));
    }

    private static async Task<AorDataset> HeavyDataset(int zones, int verticesPerZone)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < zones; i++)
        {
            if (i > 0) sb.Append(',');
            var rng = new Random(i);
            double cx = 10 + rng.NextDouble() * 20, cy = 47 + rng.NextDouble() * 13, radius = 0.05 + rng.NextDouble() * 0.5;
            sb.Append("{\"identifier\":\"Z").Append(i).Append("\",\"name\":\"Zone ").Append(i)
              .Append("\",\"geometry\":[{\"lowerLimit\":0,\"upperLimit\":1000,\"horizontalProjection\":{\"type\":\"Polygon\",\"coordinates\":[[");
            var ring = Circle(verticesPerZone, radius, cx, cy);
            for (int p = 0; p < ring.Length; p++)
            {
                if (p > 0) sb.Append(',');
                sb.Append('[').Append(ring[p][0].ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
                  .Append(ring[p][1].ToString("0.######", CultureInfo.InvariantCulture)).Append(']');
            }
            sb.Append("]]}}]}");
        }
        sb.Append(']');
        return await AorImporter.ImportAsync(ImportSource.FromText("heavy.json", sb.ToString()), null, default);
    }

    [Fact]
    public async Task ZoomedOutPayload_OfVertexHeavyData_IsBoundedByDetailNotJustFeatureCount()
    {
        // 1,600 zones x 800 vertices = 1.3M vertices: over the viewport threshold, and ~28 MB of GeoJSON if sent as-is.
        var dataset = await HeavyDataset(1600, 800);

        var payload = MapPayloadBuilder.ForAors(dataset, null, dataset.Items.Length, new HashSet<string>(), null, true, 1, default);

        long bytes = payload.Chunks.Sum(c => (long)c.Length);
        Assert.True(payload.ViewportMode);
        Assert.True(payload.Shown > 1000, $"expected most of the biggest zones to still be drawn, got {payload.Shown}");
        Assert.True(bytes < 8_000_000, $"payload was {bytes / 1048576.0:0.0} MB");
    }

    [Fact]
    public async Task ZoomedInPayload_KeepsFullDetail()
    {
        var dataset = await HeavyDataset(1600, 800);
        var zoomed = dataset.Items[0].Bounds; // a view about the size of one zone

        var payload = MapPayloadBuilder.ForAors(dataset, null, dataset.Items.Length, new HashSet<string>(), zoomed, false, 2, default);

        Assert.True(payload.Shown >= 1);
        // The zone under the view is drawn with (close to) all of its 801 vertices, not a simplified handful.
        Assert.Contains(payload.Chunks, c => c.Split("],[").Length > 600);
    }

    [Fact]
    public async Task VertexBudget_StopsFeatureOutput_AndReportsWhatWasWritten()
    {
        var dataset = await HeavyDataset(50, 400);
        var detail = new MapDetail { Tolerance = 0, VertexBudget = 2000 };

        var chunks = MapGeoJson.BuildChunks(Array.Empty<ParsedOps>(), dataset.Items, null, null, detail: detail);

        Assert.True(detail.Truncated);
        Assert.InRange(detail.ItemsWritten, 1, 49);
        Assert.InRange(detail.VerticesWritten, 2000, 2000 + 401); // stops after the feature that crosses the budget
        Assert.Single(chunks);
    }
}
