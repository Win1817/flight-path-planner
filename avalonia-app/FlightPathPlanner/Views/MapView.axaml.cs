using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
#if USE_CEF
using Xilium.CefGlue.Avalonia;
#endif

namespace FlightPathPlanner.Views;

/// <summary>Hosts the MapLibre page. Windows uses the system's Edge WebView2 (<see cref="WebView2Host"/>);
/// Linux/macOS use embedded Chromium (CefGlue). Both drive the same Assets/map page.</summary>
public partial class MapView : UserControl
{
#if USE_CEF
    private readonly AvaloniaCefBrowser? _cef;
#endif
    private readonly WebView2Host? _web;
    private bool _pageReady;
    private string? _lastData;
    private IReadOnlyCollection<string>? _lastHighlights;

    public event Action<string, string>? ZoneClicked; // (id, dataType: "ops" | "aor")
    public event Action<string?>? ZoneHovered;         // id, or null when hover ends

    public MapView()
    {
        InitializeComponent();

        if (Program.MapDisabled)
        {
            ShowMessage("Map disabled (FPP_NO_MAP=1)");
        }
        else if (OperatingSystem.IsWindows())
        {
            _web = new WebView2Host();
            _web.MessageReceived += OnWebMessage;
            _web.Failed += error =>
            {
                Log($"WebView2 failed: {error}");
                Dispatcher.UIThread.Post(() => ShowMessage(
                    "The map needs the Microsoft Edge WebView2 Runtime.\nInstall it from https://developer.microsoft.com/microsoft-edge/webview2/\n\n" + error));
            };
            RootGrid.Children.Add(_web);
        }
#if USE_CEF
        else
        {
            _cef = new AvaloniaCefBrowser();
            _cef.RegisterJavascriptObject(new JsBridge(this), "csharpBridge");
            _cef.ConsoleMessage += (_, args) => Log($"console: {args.Message} ({args.Source}:{args.Line})");
            _cef.LoadError += (_, args) => Log($"load error: {args.ErrorText} {args.FailedUrl}");
            _cef.LoadEnd += (_, _) => Dispatcher.UIThread.Post(OnPageReady);

            var mapHtmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "map", "index.html");
            _cef.Address = new Uri(mapHtmlPath).AbsoluteUri;
            RootGrid.Children.Add(_cef);
        }
#endif
    }

    private void ShowMessage(string text)
    {
        RootGrid.Children.Clear();
        RootGrid.Children.Add(new TextBlock
        {
            Text = text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
        });
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "map.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}"); }
        catch { /* logging must never take the app down */ }
    }

    private void OnPageReady()
    {
        _pageReady = true;
        if (_lastData != null) UpdateData(_lastData);
        if (_lastHighlights != null) UpdateHighlights(_lastHighlights);
    }

    private void OnWebMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (root.GetProperty("kind").GetString())
            {
                case "ready":
                    OnPageReady();
                    break;
                case "click":
                    ZoneClicked?.Invoke(root.GetProperty("id").GetString() ?? "", root.GetProperty("dataType").GetString() ?? "");
                    break;
                case "hover":
                    var id = root.GetProperty("id").GetString();
                    ZoneHovered?.Invoke(string.IsNullOrEmpty(id) ? null : id);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"bad web message: {ex.Message}");
        }
    }

    public void UpdateData(string viewerGeoJson)
    {
        _lastData = viewerGeoJson;
        if (!_pageReady) return;
        if (_web != null) _web.Post("data", viewerGeoJson);
#if USE_CEF
        else _cef?.ExecuteJavaScript($"window.updateMapData({JsonSerializer.Serialize(viewerGeoJson)});", null, 0);
#endif
    }

    public void UpdateHighlights(IReadOnlyCollection<string> ids)
    {
        _lastHighlights = ids;
        if (!_pageReady) return;
        var idsJson = JsonSerializer.Serialize(ids);
        if (_web != null) _web.Post("highlights", idsJson);
#if USE_CEF
        else _cef?.ExecuteJavaScript($"window.updateHighlights({JsonSerializer.Serialize(idsJson)});", null, 0);
#endif
    }

#if USE_CEF
    /// <summary>Object exposed to JS as window.csharpBridge (CEF only) — its public methods are callable from map.js.</summary>
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
#endif
}
