using Avalonia;
using System;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace FlightPathPlanner;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Run(args);
    }

    private static void Run(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject?.ToString() ?? "unknown");
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash("UnobservedTask: " + e.Exception);

        try
        {
            // Windows uses the system WebView2 for the map, so CEF (and its helper processes) is only started elsewhere.
            if (!MapDisabled && !OperatingSystem.IsWindows()) InitializeCef();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // WinExe has no console, so startup failures would otherwise vanish silently.
            LogCrash(ex.ToString());
            throw;
        }
    }

    public static bool MapDisabled => Environment.GetEnvironmentVariable("FPP_NO_MAP") == "1";

    private static void LogCrash(string text) =>
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"[{DateTime.Now:O}] {text}{Environment.NewLine}{Environment.NewLine}");

    private static void InitializeCef()
    {
        var resourcesDir = Path.Combine(AppContext.BaseDirectory, "Resources");
        var settings = new CefSettings
        {
            // Linux needs a SUID-root helper for the sandbox, which a portable app can't install; on Windows
            // the sandboxed GPU/network helpers were crashing at startup. The app only loads its own bundled
            // map page plus map tiles, so the sandbox is disabled everywhere.
            NoSandbox = true,
            LogSeverity = CefLogSeverity.Warning,
            LogFile = Path.Combine(AppContext.BaseDirectory, "cef.log"),
        };

        // Explicit resource paths were needed for the Linux layout; on Windows/macOS CefGlue
        // copies resources next to the exe and its own defaults must be used.
        if (OperatingSystem.IsLinux())
        {
            settings.ResourcesDirPath = resourcesDir;
            settings.LocalesDirPath = Path.Combine(resourcesDir, "locales");
        }

        // The GPU process crashes repeatedly on some Windows GPU/driver combinations (CEF then aborts with
        // "GPU process isn't usable"), so use software rendering; the map still works through SwiftShader WebGL.
        var extraArgs = new List<KeyValuePair<string, string>>
        {
            new("disable-gpu", ""),
            new("disable-gpu-compositing", ""),
            new("enable-unsafe-swiftshader", ""),
            // Keep GPU/network work inside the browser process: their separate helper processes kept crashing.
            new("in-process-gpu", ""),
            new("enable-features", "NetworkServiceInProcess2"),
        };

        if (OperatingSystem.IsLinux())
        {
            // "no-zygote": the zygote pre-fork optimization needs sandbox/namespace support that
            // container-like environments lack; "single-process" and friends avoid the same class of crash.
            extraArgs.Add(new("no-zygote", ""));
            extraArgs.Add(new("disable-software-rasterizer", ""));
            extraArgs.Add(new("single-process", ""));
            extraArgs.Add(new("disable-dev-shm-usage", ""));
        }

        CefRuntimeLoader.Initialize(settings, extraArgs.ToArray());
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
