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

        _browser.ConsoleMessage += (_, args) =>
        {
            // Surface JS errors for debugging; the map page has no other error channel.
            Console.WriteLine($"[map console] {args.Message} ({args.Source}:{args.Line})");
        };

        var mapHtmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "map", "index.html");
        _browser.Address = new Uri(mapHtmlPath).AbsoluteUri;

        RootGrid.Children.Add(_browser);
    }

    public void UpdateData(string viewerGeoJson)
    {
        var script = $"window.updateMapData({JsonSerializer.Serialize(viewerGeoJson)});";
        _browser?.ExecuteJavaScript(script, null, 0);
    }

    public void UpdateHighlights(IReadOnlyCollection<string> ids)
    {
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
