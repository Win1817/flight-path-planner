using FlightPathPlanner.Models;

namespace FlightPathPlanner.ViewModels;

public sealed class ReportAorOptionViewModel(ParsedAor aor)
{
    public ParsedAor Aor { get; } = aor;
    public string Id => Aor.Id;
    public string Name => Aor.Name;
    public string Designator => Aor.Designator;
    public string DisplayLabel => $"{Aor.Name} ({Aor.Designator})";
}
