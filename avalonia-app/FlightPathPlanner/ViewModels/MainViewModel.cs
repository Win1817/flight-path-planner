using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FlightPathPlanner.ViewModels;

public enum AppTab
{
    Ops,
    Aors,
    Report,
    Lookup,
}

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial AppTab ActiveTab { get; set; } = AppTab.Ops;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public OpsTabViewModel OpsTab { get; } = new();
    public AorTabViewModel AorTab { get; } = new();
    public ReportTabViewModel ReportTab { get; }
    public LookupTabViewModel LookupTab { get; }

    public MainViewModel()
    {
        ReportTab = new ReportTabViewModel(OpsTab, AorTab);
        LookupTab = new LookupTabViewModel(OpsTab);
    }

    [RelayCommand]
    private void SelectTab(AppTab tab) => ActiveTab = tab;
}
