using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FlightPathPlanner.Services;
#if USE_CEF
using Xilium.CefGlue.Avalonia;
#endif

namespace FlightPathPlanner.Views;

/// <summary>Hosts the MapLibre page. Windows uses the system's Edge WebView2 (<see cref="WebView2Host"/>);
/// Linux/macOS use embedded Chromium (CefGlue). Both drive the same embedded map page through the same JSON messages.</summary>
public partial class MapView : UserControl
{
#if USE_CEF
    private readonly AvaloniaCefBrowser? _cef;
#endif
    private readonly WebView2Host? _web;
    private bool _pageReady;
    private MapPayload? _lastPayload;
    private IReadOnlyCollection<string>? _lastHighlights;

    public event Action<string, string>? ZoneClicked;               // (id, dataType: "ops" | "aor")
    public event Action<string?>? ZoneHovered;                       // id, or null when hover ends
    public event Action<double, double, double, double>? ViewportChanged; // west, south, east, north

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

            var mapHtmlPath = Path.Combine(FlightPathPlanner.Services.MapAssets.EnsureExtracted(), "index.html");
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
        if (_lastPayload != null) Send(_lastPayload);
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
                case "viewport":
                    ViewportChanged?.Invoke(
                        root.GetProperty("west").GetDouble(), root.GetProperty("south").GetDouble(),
                        root.GetProperty("east").GetDouble(), root.GetProperty("north").GetDouble());
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"bad web message: {ex.Message}");
        }
    }

    /// <summary>Sends an update to the page as begin / chunk... / end messages, so a large update is many modest messages rather
    /// than one giant string. Remembers the latest payload to replay if the page isn't ready yet.</summary>
    public void PushPayload(MapPayload payload)
    {
        _lastPayload = payload;
        if (_pageReady) Send(payload);
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Send(MapPayload p)
    {
        var begin = new StringBuilder(256);
        begin.Append("{\"kind\":\"begin\",\"version\":").Append(p.Version)
             .Append(",\"viewportMode\":").Append(p.ViewportMode ? "true" : "false")
             .Append(",\"fit\":").Append(p.Fit ? "true" : "false")
             .Append(",\"fitBounds\":").Append(p.FitBounds is { Length: 4 } b ? $"[{Num(b[0])},{Num(b[1])},{Num(b[2])},{Num(b[3])}]" : "null")
             .Append(",\"shown\":").Append(p.Shown)
             .Append(",\"inView\":").Append(p.InView)
             .Append(",\"total\":").Append(p.Total)
             .Append(",\"anyHighlight\":").Append(p.AnyHighlight ? "true" : "false")
             .Append(",\"needsViewport\":").Append(p.NeedsViewport ? "true" : "false")
             .Append('}');
        Post(begin.ToString());

        foreach (var chunk in p.Chunks)
            Post($"{{\"kind\":\"chunk\",\"version\":{p.Version},\"features\":{chunk}}}");

        Post($"{{\"kind\":\"end\",\"version\":{p.Version}}}");
    }

    /// <summary>Hovered / active ids to emphasise (selection is flagged inside the map data itself).</summary>
    public void UpdateHighlights(IReadOnlyCollection<string> ids)
    {
        _lastHighlights = ids;
        if (_pageReady) Post($"{{\"kind\":\"highlights\",\"ids\":{JsonSerializer.Serialize(ids)}}}");
    }

    private void Post(string json)
    {
        if (_web != null) { _web.PostJson(json); return; }
#if USE_CEF
        _cef?.ExecuteJavaScript($"window.lunaHost({JsonSerializer.Serialize(json)});", null, 0);
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

        public void OnViewport(double west, double south, double east, double north)
        {
            Dispatcher.UIThread.Post(() => owner.ViewportChanged?.Invoke(west, south, east, north));
        }
    }
#endif
}
