using CommunityToolkit.Mvvm.ComponentModel;

namespace FlightPathPlanner.ViewModels;

public partial class ClosureReasonChipViewModel(string reason) : ViewModelBase
{
    public string Reason { get; } = reason;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
