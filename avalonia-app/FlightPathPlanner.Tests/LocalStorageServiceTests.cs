using FlightPathPlanner.Services;

namespace FlightPathPlanner.Tests;

public sealed class LocalStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fpp-storage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Save_WritesTimestampedFileIntoCategoryFolder_AndRoundTrips()
    {
        var storage = new LocalStorageService(_root);

        var saved = storage.Save(StorageCategory.Ops, "flights.json", "{\"a\":1}");

        Assert.StartsWith(Path.Combine(_root, "OPS"), saved.FullPath);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-\d{3}Z__flights\.json$", saved.FileName);
        Assert.Equal("flights.json", saved.DisplayName);
        Assert.NotNull(saved.SavedAtUtc);
        Assert.Equal("{\"a\":1}", storage.ReadText(saved));
    }

    [Fact]
    public void SanitizeFileName_ReplacesForbiddenCharacters_AndStripsPaths()
    {
        Assert.Equal("a_b_c.json", LocalStorageService.SanitizeFileName("a:b?c.json"));
        Assert.Equal("evil.json", LocalStorageService.SanitizeFileName("../../evil.json"));
        Assert.Equal("data.json", LocalStorageService.SanitizeFileName("   "));
    }

    [Fact]
    public void List_ReturnsNewestFirst_AndSeparatesActiveFromArchived()
    {
        var storage = new LocalStorageService(_root);
        var first = storage.Save(StorageCategory.Aor, "first.json", "1");
        Thread.Sleep(5);
        var second = storage.Save(StorageCategory.Aor, "second.json", "2");

        Assert.Equal(new[] { second.FileName, first.FileName }, storage.List(StorageCategory.Aor, archived: false).Select(f => f.FileName));

        storage.Archive(first);

        Assert.Single(storage.List(StorageCategory.Aor, archived: false));
        Assert.Equal(first.FileName, storage.List(StorageCategory.Aor, archived: true).Single().FileName);
    }

    [Fact]
    public void ArchiveThenUnarchive_MovesFileBackWithContentIntact()
    {
        var storage = new LocalStorageService(_root);
        var saved = storage.Save(StorageCategory.Report, "r.json", "report");

        var archived = storage.Archive(saved);
        Assert.True(archived.IsArchived);
        Assert.True(File.Exists(Path.Combine(_root, "Report", "Archive", saved.FileName)));
        Assert.False(File.Exists(saved.FullPath));

        var restored = storage.Unarchive(archived);
        Assert.False(restored.IsArchived);
        Assert.Equal("report", storage.ReadText(restored));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var storage = new LocalStorageService(_root);
        var saved = storage.Save(StorageCategory.Ops, "x.json", "x");

        storage.Delete(saved);

        Assert.Empty(storage.List(StorageCategory.Ops, archived: false));
    }

    [Fact]
    public void Operations_RejectPathTraversalInFileName()
    {
        var storage = new LocalStorageService(_root);
        var evil = new SavedFile(StorageCategory.Ops, "../secret.json", false, "", 0);

        Assert.Throws<InvalidOperationException>(() => storage.ReadText(evil));
        Assert.Throws<InvalidOperationException>(() => storage.Delete(evil));
    }

    [Fact]
    public void List_OnMissingFolder_IsEmpty()
    {
        Assert.Empty(new LocalStorageService(_root).List(StorageCategory.Ops, archived: true));
    }
}
