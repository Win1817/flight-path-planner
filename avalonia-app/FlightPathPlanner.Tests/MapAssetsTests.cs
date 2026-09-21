using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public sealed class MapAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fpp-map-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void EnsureExtracted_UnpacksTheEmbeddedMapPage()
    {
        var dir = MapAssets.EnsureExtracted(_root);

        foreach (var file in new[] { "index.html", "map.js", "maplibre-gl.js", "maplibre-gl.css" })
            Assert.True(File.Exists(Path.Combine(dir, file)), $"{file} should be extracted");
        Assert.Contains("maplibre-gl.js", File.ReadAllText(Path.Combine(dir, "index.html")));
    }

    [Fact]
    public void EnsureExtracted_IsIdempotent_AndLeavesExistingFilesAlone()
    {
        var first = MapAssets.EnsureExtracted(_root);
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(first, "map.js"));
        Thread.Sleep(20);

        var second = MapAssets.EnsureExtracted(_root);

        Assert.Equal(first, second);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(second, "map.js")));
    }

    [Fact]
    public void EnsureExtracted_RemovesFoldersFromOtherBuilds()
    {
        var stale = Path.Combine(_root, "0000oldbuild");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "index.html"), "old");

        var dir = MapAssets.EnsureExtracted(_root);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(dir));
    }
}
