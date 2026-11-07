using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Calisto;

/// <summary>Full-screen, click-through overlay: green radar rings ripple out from the Calisto icon.</summary>
public partial class ScanOverlayWindow : Window
{
    private readonly Point _centerDip;

    public ScanOverlayWindow(Point centerDip)
    {
        InitializeComponent();
        _centerDip = centerDip;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this,
            NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        Loaded += (_, _) => StartAnimation();
    }

    private void StartAnimation()
    {
        var center = new Point(_centerDip.X - Left, _centerDip.Y - Top);
        // Far enough to reach the farthest screen corner.
        double maxR = new[]
        {
            Distance(center, new Point(0, 0)), Distance(center, new Point(Width, 0)),
            Distance(center, new Point(0, Height)), Distance(center, new Point(Width, Height))
        }.Max() + 20;

        var period = TimeSpan.FromSeconds(2.0);
        var stagger = TimeSpan.FromSeconds(0.7);
        Animate(Ring1, Ring1Path, center, maxR, period, TimeSpan.Zero);
        Animate(Ring2, Ring2Path, center, maxR, period, stagger);
        Animate(Ring3, Ring3Path, center, maxR, period, stagger + stagger);

        var pulse = new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        Pulse.BeginAnimation(OpacityProperty, pulse);
    }

    private static void Animate(EllipseGeometry ring, Path path, Point center, double maxR, TimeSpan period, TimeSpan delay)
    {
        ring.Center = center;
        path.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var grow = new DoubleAnimation(4, maxR, period) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        ring.BeginAnimation(EllipseGeometry.RadiusXProperty, grow);
        ring.BeginAnimation(EllipseGeometry.RadiusYProperty, grow);

        var fade = new DoubleAnimation(0.9, 0, period) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever };
        path.BeginAnimation(OpacityProperty, fade);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
