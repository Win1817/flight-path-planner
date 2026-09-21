using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FlightPathPlanner.Services;

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

    public static Task SaveJsonAsync(Control owner, string title, string suggestedName, Func<string> buildJson) =>
        SaveAsync(owner, title, suggestedName, JsonType, async stream =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(buildJson());
        });

    public static Task SaveXlsxAsync(Control owner, string title, string suggestedName, Func<byte[]> buildBytes) =>
        SaveAsync(owner, title, suggestedName, XlsxType, async stream => await stream.WriteAsync(buildBytes()));

    private static async Task SaveAsync(Control owner, string title, string suggestedName, FilePickerFileType type, Func<Stream, Task> write)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { type },
        });
        if (file == null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await write(stream);
            Notifier.Success($"Exported {file.Name}");
        }
        catch (Exception ex)
        {
            Notifier.Error($"Export failed: {ex.Message}");
        }
    }
}
