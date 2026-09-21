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
        try
        {
            InitializeCef();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // WinExe has no console, so startup failures would otherwise vanish silently.
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), ex.ToString());
            throw;
        }
    }

    private static void InitializeCef()
    {
        var resourcesDir = Path.Combine(AppContext.BaseDirectory, "Resources");
        var settings = new CefSettings
        {
            // CEF's sandbox needs a SUID-root helper binary (chrome-sandbox) that a portable,
            // no-install app cannot set up (that requires a privileged one-time chown/chmod
            // step). Disabling it on Linux is the deliberate tradeoff for staying fully
            // portable there; Windows/macOS sandboxing doesn't need this and stays enabled.
            NoSandbox = OperatingSystem.IsLinux(),
        };

        // Explicit resource paths were needed for the Linux layout; on Windows/macOS CefGlue
        // copies resources next to the exe and its own defaults must be used.
        if (OperatingSystem.IsLinux())
        {
            settings.ResourcesDirPath = resourcesDir;
            settings.LocalesDirPath = Path.Combine(resourcesDir, "locales");
        }

        var extraArgs = OperatingSystem.IsLinux()
            ? new[]
            {
                // "no-zygote": the zygote pre-fork optimization needs sandbox/namespace
                // support this container-like environment doesn't have, and crashes early
                // (the "Invalid file descriptor to ICU data received" trap) without it.
                new KeyValuePair<string, string>("no-zygote", ""),
                new KeyValuePair<string, string>("disable-gpu", ""),
                new KeyValuePair<string, string>("disable-gpu-compositing", ""),
                new KeyValuePair<string, string>("disable-software-rasterizer", ""),
                new KeyValuePair<string, string>("single-process", ""),
                new KeyValuePair<string, string>("disable-dev-shm-usage", ""),
            }
            : Array.Empty<KeyValuePair<string, string>>();

        CefRuntimeLoader.Initialize(settings, extraArgs);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
