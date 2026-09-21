using Avalonia;
using Avalonia.Controls;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace FlightPathPlanner.Controls;

/// <summary>A monochrome line icon. All Luna icons are drawn on a 24x24 grid (see Styles/LunaIcons.axaml) and scaled to
/// the control's size, so every icon keeps the same optical weight regardless of its own bounds.</summary>
public class LunaIcon : Viewbox
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LunaIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LunaIcon, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LunaIcon, double>(nameof(StrokeWidth), 2);

    private readonly ShapePath _path = new()
    {
        Stretch = Stretch.None,
        StrokeThickness = 2, // matches StrokeWidth's default, which raises no change notification
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    };

    public LunaIcon()
    {
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Child = new Canvas { Width = 24, Height = 24, Children = { _path } };
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty) _path.Data = Data;
        else if (change.Property == StrokeProperty) _path.Stroke = Stroke;
        else if (change.Property == StrokeWidthProperty) _path.StrokeThickness = StrokeWidth;
    }
}
