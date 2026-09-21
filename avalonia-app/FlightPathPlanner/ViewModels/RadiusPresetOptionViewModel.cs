using CommunityToolkit.Mvvm.ComponentModel;

namespace FlightPathPlanner.ViewModels;

public partial class RadiusPresetOptionViewModel(double km) : ViewModelBase
{
    public double Km { get; } = km;
    public string Label => $"{Km:0.#} km";

    /// <summary>True while the lookup radius equals this preset.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
