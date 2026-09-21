using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;

namespace FlightPathPlanner.Views;

/// <summary>Hosts the Edge WebView2 control (preinstalled on Windows 10/11) inside Avalonia as a native child window.
/// Used on Windows instead of CEF, whose helper processes crash on some machines.</summary>
public sealed class WebView2Host : NativeControlHost
{
    private const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000, WsClipChildren = 0x02000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private IntPtr _hwnd;
    private CoreWebView2Controller? _controller;

    /// <summary>Raised on the UI thread with each string message the page posts (see map.js).</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Raised if the WebView2 runtime could not be started.</summary>
    public event Action<string>? Failed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows()) return base.CreateNativeControlCore(parent);

        _hwnd = CreateWindowEx(0, "STATIC", "", WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _ = InitializeAsync();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!OperatingSystem.IsWindows()) { base.DestroyNativeControlCore(control); return; }
        _controller?.Close();
        _controller = null;
        DestroyWindow(control.Handle);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            Dispatcher.UIThread.Post(UpdateBounds, DispatcherPriority.Background);
    }

    private void UpdateBounds()
    {
        if (_controller == null || _hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out var r)) return;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, r.Right - r.Left, r.Bottom - r.Top);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UasTool", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            _controller = await env.CreateCoreWebView2ControllerAsync(_hwnd);
            _controller.IsVisible = true;
            UpdateBounds();

            var web = _controller.CoreWebView2;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.SetVirtualHostNameToFolderMapping("appassets.local",
                Path.Combine(AppContext.BaseDirectory, "Assets", "map"), CoreWebView2HostResourceAccessKind.Allow);
            web.WebMessageReceived += (_, e) => MessageReceived?.Invoke(e.TryGetWebMessageAsString());
            web.Navigate("https://appassets.local/index.html");
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    public void Post(string kind, string payload)
    {
        _controller?.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(new { kind, payload }));
    }
}
