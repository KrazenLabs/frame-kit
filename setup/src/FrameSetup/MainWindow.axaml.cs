using Avalonia;
using Avalonia.Controls;

namespace FrameKit.Setup.App;

public sealed partial class MainWindow : Window
{
    private const double FallbackChromeHeight = 40;
    private double _chromeHeight = FallbackChromeHeight;

    public static Uri FrameKitPage { get; } = new("https://example.com/framekit");

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => LimitToScreen();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty && IsVisible && change.OldValue is Size before && change.NewValue is Size after)
        {
            KeepCentered(before.Height, after.Height);
        }
    }

    private void LimitToScreen()
    {
        if (FrameSize is { } frame && frame.Height > ClientSize.Height)
        {
            _chromeHeight = frame.Height - ClientSize.Height;
        }
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            MaxHeight = Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - _chromeHeight);
        }
    }

    private void KeepCentered(double before, double after)
    {
        if (Math.Abs(after - before) < 1 || Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }
        var area = screen.WorkingArea;
        var frameHeight = (int)Math.Ceiling((after + _chromeHeight) * screen.Scaling);
        var top = Position.Y - (int)Math.Round((after - before) * screen.Scaling / 2);
        top = Math.Clamp(top, area.Y, Math.Max(area.Y, area.Bottom - frameHeight));
        Position = new PixelPoint(Position.X, top);
    }
}
