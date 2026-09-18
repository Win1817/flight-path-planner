namespace FlightPathPlanner.ViewModels;

public sealed class RadiusPresetOptionViewModel(double km)
{
    public double Km { get; } = km;
    public string Label => $"{Km:0.#}km";
}
