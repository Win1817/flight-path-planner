using System.Reflection;

namespace FlightPathPlanner.Services;

/// <summary>The map page (index.html, map.js, MapLibre) is embedded in the executable. Web views load real files, so it is
/// unpacked once per build to <c>%LOCALAPPDATA%\UasTool\map-assets\&lt;build id&gt;</c> (or the platform equivalent).</summary>
public static class MapAssets
{
    private const string Prefix = "map/";
    private const string CompleteMarker = ".complete";

    /// <summary>Extracts (if needed) and returns the folder that contains <c>index.html</c>.</summary>
    /// <param name="rootDirectory">Override for tests; defaults to the user's local application data.</param>
    public static string EnsureExtracted(string? rootDirectory = null)
    {
        var assembly = typeof(MapAssets).Assembly;
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UasTool", "map-assets");
        var buildId = assembly.ManifestModule.ModuleVersionId.ToString("N");
        var directory = Path.Combine(root, buildId);

        if (!File.Exists(Path.Combine(directory, CompleteMarker)))
        {
            Directory.CreateDirectory(directory);
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                using var source = assembly.GetManifestResourceStream(resource)!;
                var target = Path.Combine(directory, resource[Prefix.Length..]);
                using var destination = File.Create(target);
                source.CopyTo(destination);
            }
            File.WriteAllText(Path.Combine(directory, CompleteMarker), "");
            DeleteOtherBuilds(root, buildId);
        }

        return directory;
    }

    // Old builds' copies are only clutter; failing to remove one (e.g. still open in another instance) is harmless.
    private static void DeleteOtherBuilds(string root, string keep)
    {
        try
        {
            foreach (var other in Directory.EnumerateDirectories(root).Where(d => Path.GetFileName(d) != keep))
            {
                try { Directory.Delete(other, recursive: true); } catch { /* in use */ }
            }
        }
        catch { /* best effort */ }
    }
}
