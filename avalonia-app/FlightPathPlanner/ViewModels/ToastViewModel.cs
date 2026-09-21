using FlightPathPlanner.Services;

namespace FlightPathPlanner.ViewModels;

public sealed class ToastViewModel(NotificationKind kind, string message) : ViewModelBase
{
    public NotificationKind Kind { get; } = kind;
    public string Message { get; } = message;
    public bool IsSuccess => Kind == NotificationKind.Success;
    public bool IsInfo => Kind == NotificationKind.Info;
    public bool IsWarning => Kind == NotificationKind.Warning;
    public bool IsError => Kind == NotificationKind.Error;

    /// <summary>Text for assistive technology: the icon alone would identify the kind only by colour/shape.</summary>
    public string KindLabel => Kind switch
    {
        NotificationKind.Success => "Success",
        NotificationKind.Warning => "Warning",
        NotificationKind.Error => "Error",
        _ => "Information",
    };
}
