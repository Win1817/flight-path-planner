namespace FlightPathPlanner.Services;

/// <summary>Appends one-line diagnostics (such as an import's timings) to <c>import.log</c> beside the executable. Never throws.</summary>
public static class AppLog
{
    public static void Diagnostic(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "import.log"), $"[{DateTime.Now:O}] {line}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }
}
