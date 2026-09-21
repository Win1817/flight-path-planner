using System.Globalization;
using System.Text;

namespace FlightPathPlanner.Services;

public enum StorageCategory { Ops, Aor, Report }

public sealed record SavedFile(StorageCategory Category, string FileName, bool IsArchived, string FullPath, long SizeBytes)
{
    private const string Separator = "__";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH-mm-ss-fff'Z'";

    /// <summary>The original file name, without the timestamp prefix added when it was saved.</summary>
    public string DisplayName
    {
        get
        {
            int i = FileName.IndexOf(Separator, StringComparison.Ordinal);
            return i > 0 ? FileName[(i + Separator.Length)..] : FileName;
        }
    }

    public DateTime? SavedAtUtc
    {
        get
        {
            int i = FileName.IndexOf(Separator, StringComparison.Ordinal);
            return i > 0 && DateTime.TryParseExact(FileName[..i], TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
        }
    }
}

/// <summary>Keeps uploaded/exported data as plain files under a portable data folder:
/// <c>data/OPS|AoR|Report/</c> with an <c>Archive/</c> subfolder in each. Replaces the web app's dev-server plugin,
/// so no HTTP layer is needed.</summary>
public sealed class LocalStorageService
{
    private const string ArchiveFolder = "Archive";
    private const string PartialSuffix = ".partial";
    private readonly string _root;

    public LocalStorageService(string rootDirectory)
    {
        _root = rootDirectory;
    }

    public string RootDirectory => _root;

    /// <summary>Uses a "data" folder next to the executable (keeping the app portable, e.g. on a USB drive), falling back
    /// to the user's local app data when that location isn't writable (e.g. Program Files).</summary>
    public static LocalStorageService CreateDefault()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "data");
        if (IsWritable(beside)) return new LocalStorageService(beside);
        return new LocalStorageService(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UasTool", "data"));
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FolderName(StorageCategory category) => category switch
    {
        StorageCategory.Ops => "OPS",
        StorageCategory.Aor => "AoR",
        _ => "Report",
    };

    private string CategoryDirectory(StorageCategory category, bool archived)
    {
        var dir = Path.Combine(_root, FolderName(category));
        return archived ? Path.Combine(dir, ArchiveFolder) : dir;
    }

    /// <summary>Replaces characters that are illegal in Windows file names (and control characters), keeping saved
    /// files usable on every OS.</summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in Path.GetFileName(name.Replace('\\', '/')))
            sb.Append(c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c);
        var cleaned = sb.ToString().Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "data.json" : cleaned;
    }

    public SavedFile Save(StorageCategory category, string originalName, string content)
    {
        var dir = CategoryDirectory(category, archived: false);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss-fff'Z'", CultureInfo.InvariantCulture);
        var fileName = $"{stamp}__{SanitizeFileName(originalName)}";
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return new SavedFile(category, fileName, false, path, new FileInfo(path).Length);
    }

    /// <summary>Copies a source stream into the category folder without holding it in memory. The copy is written to a temporary
    /// name and moved into place only when complete, so an interrupted or cancelled save never leaves a truncated file behind.</summary>
    public async Task<SavedFile> SaveStreamAsync(StorageCategory category, string originalName, Func<Stream> openSource, CancellationToken ct = default)
    {
        var dir = CategoryDirectory(category, archived: false);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss-fff'Z'", CultureInfo.InvariantCulture);
        var fileName = $"{stamp}__{SanitizeFileName(originalName)}";
        var path = Path.Combine(dir, fileName);
        var temp = path + PartialSuffix;

        try
        {
            await using (var source = openSource())
            await using (var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await source.CopyToAsync(destination, 1 << 16, ct).ConfigureAwait(false);
            }
            File.Move(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
        return new SavedFile(category, fileName, false, path, new FileInfo(path).Length);
    }

    /// <summary>Opens a saved file for streaming reads.</summary>
    public Stream OpenRead(SavedFile file) =>
        new FileStream(Resolve(file), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);

    /// <summary>Saved files for a category, newest first.</summary>
    public IReadOnlyList<SavedFile> List(StorageCategory category, bool archived)
    {
        var dir = CategoryDirectory(category, archived);
        if (!Directory.Exists(dir)) return Array.Empty<SavedFile>();

        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => !f.Name.StartsWith('.') && !f.Name.EndsWith(PartialSuffix, StringComparison.Ordinal))
            .Select(f => new SavedFile(category, f.Name, archived, f.FullName, f.Length))
            .OrderByDescending(f => f.SavedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(f => f.FileName, StringComparer.Ordinal)
            .ToList();
    }

    public string ReadText(SavedFile file) => File.ReadAllText(Resolve(file));

    public SavedFile Archive(SavedFile file) => Move(file, toArchive: true);

    public SavedFile Unarchive(SavedFile file) => Move(file, toArchive: false);

    public void Delete(SavedFile file) => File.Delete(Resolve(file));

    private SavedFile Move(SavedFile file, bool toArchive)
    {
        var source = Resolve(file);
        var targetDir = CategoryDirectory(file.Category, toArchive);
        Directory.CreateDirectory(targetDir);
        var target = Path.Combine(targetDir, file.FileName);
        File.Move(source, target, overwrite: true);
        return file with { IsArchived = toArchive, FullPath = target };
    }

    /// <summary>Re-derives the path from the category/name rather than trusting <see cref="SavedFile.FullPath"/>, and
    /// rejects anything that isn't a plain file name inside the expected folder.</summary>
    private string Resolve(SavedFile file)
    {
        if (file.FileName != Path.GetFileName(file.FileName) || file.FileName.Contains(".."))
            throw new InvalidOperationException($"Invalid saved file name: {file.FileName}");
        return Path.Combine(CategoryDirectory(file.Category, file.IsArchived), file.FileName);
    }
}
