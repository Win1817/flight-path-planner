using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;

namespace FlightPathPlanner.Views;

public partial class MapView : UserControl
{
    private readonly AvaloniaCefBrowser? _browser;

    public event Action<string, string>? ZoneClicked; // (id, dataType: "ops" | "aor")
    public event Action<string?>? ZoneHovered;         // id, or null when hover ends

    public MapView()
    {
        InitializeComponent();

        if (Program.MapDisabled)
        {
            RootGrid.Children.Add(new TextBlock
            {
                Text = "Map disabled (FPP_NO_MAP=1)",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Avalonia.Media.Brushes.Gray,
            });
            return;
        }

        _browser = new AvaloniaCefBrowser();
        _browser.RegisterJavascriptObject(new JsBridge(this), "csharpBridge");

        _browser.ConsoleMessage += (_, args) => Log($"console: {args.Message} ({args.Source}:{args.Line})");
        _browser.LoadError += (_, args) => Log($"load error: {args.ErrorText} {args.FailedUrl}");
        _browser.LoadStart += (_, _) => Log("load start");
        _browser.LoadEnd += (_, _) =>
        {
            Log("load end");
            Dispatcher.UIThread.Post(() =>
            {
                _pageLoaded = true;
                if (_lastData != null) UpdateData(_lastData);
                if (_lastHighlights != null) UpdateHighlights(_lastHighlights);
            });
        };
        _browser.BrowserInitialized += () => Log("browser initialized");

        var mapHtmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "map", "index.html");
        Log($"navigating to {mapHtmlPath} (exists: {File.Exists(mapHtmlPath)})");
        _browser.Address = new Uri(mapHtmlPath).AbsoluteUri;

        RootGrid.Children.Add(_browser);
    }

    private bool _pageLoaded;
    private string? _lastData;
    private IReadOnlyCollection<string>? _lastHighlights;

    private static void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "map.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}"); }
        catch { /* logging must never take the app down */ }
    }

    public void UpdateData(string viewerGeoJson)
    {
        _lastData = viewerGeoJson;
        if (!_pageLoaded) return;
        var script = $"window.updateMapData({JsonSerializer.Serialize(viewerGeoJson)});";
        _browser?.ExecuteJavaScript(script, null, 0);
    }

    public void UpdateHighlights(IReadOnlyCollection<string> ids)
    {
        _lastHighlights = ids;
        if (!_pageLoaded) return;
        var idsJson = JsonSerializer.Serialize(ids);
        var script = $"window.updateHighlights({JsonSerializer.Serialize(idsJson)});";
        _browser?.ExecuteJavaScript(script, null, 0);
    }

    /// <summary>Object exposed to JS as window.csharpBridge — its public methods are callable from map.js.</summary>
    private sealed class JsBridge(MapView owner)
    {
        public void OnZoneClick(string id, string dataType)
        {
            Dispatcher.UIThread.Post(() => owner.ZoneClicked?.Invoke(id, dataType));
        }

        public void OnZoneHover(string id)
        {
            Dispatcher.UIThread.Post(() => owner.ZoneHovered?.Invoke(string.IsNullOrEmpty(id) ? null : id));
        }
    }
}
