using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FlightPathPlanner.Services;
using FlightPathPlanner.Services.Import;

namespace FlightPathPlanner.Views;

/// <summary>The open/save dialogs shared by every tab, so success/failure feedback is consistent.</summary>
internal static class FileDialogs
{
    private static readonly FilePickerFileType JsonType = new("JSON") { Patterns = new[] { "*.json" } };
    private static readonly FilePickerFileType XlsxType = new("Excel Workbook") { Patterns = new[] { "*.xlsx" } };

    /// <summary>Lets the user choose a JSON file; returns its name and text, or null if cancelled or unreadable.</summary>
    public static async Task<(string Name, string Text)?> PickJsonAsync(Control owner, string title)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonType },
        });
        var file = files.Count > 0 ? files[0] : null;
        if (file == null) return null;

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return (file.Name, await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            Notifier.Error($"Couldn't read {file.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Lets the user choose a JSON file and returns a streaming source for it. The file is never read into a string here:
    /// the importer streams it from disk. Picker results without a local path (some sandboxed platforms) are copied to a temp file first.</summary>
    public static async Task<ImportSource?> PickJsonSourceAsync(Control owner, string title)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonType },
        });
        var file = files.Count > 0 ? files[0] : null;
        if (file == null) return null;

        try
        {
            var path = file.TryGetLocalPath();
            if (path != null) return ImportSource.FromFile(path);

            var temp = Path.Combine(Path.GetTempPath(), $"uas-import-{Guid.NewGuid():N}.json");
            await using (var input = await file.OpenReadAsync())
            await using (var output = File.Create(temp))
                await input.CopyToAsync(output);
            return new ImportSource(file.Name, new FileInfo(temp).Length, ImportSource.FromFile(temp).Open);
        }
        catch (Exception ex)
        {
            Notifier.Error($"Couldn't read {file.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Saves JSON produced by <paramref name="prepare"/>. The prepare step runs on the UI thread and must only capture
    /// state; the returned builder runs on a worker thread, so serialising a large selection never freezes the window.</summary>
    public static Task<bool> SaveJsonAsync(Control owner, string title, string suggestedName, Func<Func<string>> prepare) =>
        SaveAsync(owner, title, suggestedName, JsonType, async stream =>
        {
            var build = prepare();
            var text = await Task.Run(build);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(text);
        });

    public static Task<bool> SaveXlsxAsync(Control owner, string title, string suggestedName, Func<Func<byte[]>> prepare) =>
        SaveAsync(owner, title, suggestedName, XlsxType, async stream =>
        {
            var build = prepare();
            var bytes = await Task.Run(build);
            await stream.WriteAsync(bytes);
        });

    private static async Task<bool> SaveAsync(Control owner, string title, string suggestedName, FilePickerFileType type, Func<Stream, Task> write)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return false;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { type },
        });
        if (file == null) return false;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await write(stream);
            Notifier.Success($"Exported {file.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Notifier.Error($"Export failed: {ex.Message}");
            return false;
        }
    }
}
