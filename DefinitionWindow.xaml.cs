using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Calisto;

/// <summary>Small card that shows one word and its definition next to where the word is on screen.</summary>
public partial class DefinitionWindow : Window
{
    private static DefinitionWindow? _current;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _mouseWasDown;

    private DefinitionWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW);
        _watch.Tick += (_, _) => CloseOnOutsideClick();
        Closed += (_, _) => { _watch.Stop(); if (_current == this) _current = null; };
    }

    /// <summary>Shows the card for a word near a screen rectangle (physical pixels) or near the mouse, and looks the word up.</summary>
    public static DefinitionWindow Show(string word, string context, Rect? anchorPx)
    {
        _current?.Close();
        var w = new DefinitionWindow();
        _current = w;
        w.Word.Text = word;
        w.Body.Text = "Looking up...";
        w.Source.Text = "";
        w.Show();
        w.PlaceNear(anchorPx);
        w._watch.Start();
        w.LookupAsync(word, context);
        return w;
    }

    private async void LookupAsync(string word, string context)
    {
        var result = await AbbreviationLookup.LookupAsync(word, context);
        if (_current != this) return;
        Body.Text = result.Text;
        Source.Text = result.Source;
        UpdateLayout();
        KeepOnScreen();
    }

    private void PlaceNear(Rect? anchorPx)
    {
        UpdateLayout();
        var src = PresentationSource.FromVisual(this);
        var toDip = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point anchor;
        double below = 8;
        if (anchorPx is { } r && r.Width > 0)
        {
            anchor = toDip.Transform(new Point(r.Left, r.Bottom));
        }
        else
        {
            anchor = toDip.Transform(NativeMethods.CursorScreenPx());
            below = 16;
        }
        Left = anchor.X;
        Top = anchor.Y + below;
        KeepOnScreen(anchorPx is { } rr ? toDip.Transform(new Point(rr.Left, rr.Top)).Y : (double?)null);
    }

    private void KeepOnScreen(double? anchorTopDip = null)
    {
        var area = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width, h = ActualHeight > 0 ? ActualHeight : 120;
        if (Left + w > area.Right - 4) Left = Math.Max(area.Left + 4, area.Right - w - 4);
        if (Left < area.Left + 4) Left = area.Left + 4;
        if (Top + h > area.Bottom - 4)
            Top = anchorTopDip is double at ? Math.Max(area.Top + 4, at - h - 8) : Math.Max(area.Top + 4, area.Bottom - h - 4);
    }

    /// <summary>Shows a short hint instead of a definition (for example when nothing was selected). Closes by itself.</summary>
    public static void ShowMessage(string text)
    {
        _current?.Close();
        var w = new DefinitionWindow();
        _current = w;
        w.Word.Text = "Calisto";
        w.Body.Text = text;
        w.Show();
        w.PlaceNear(null);
        w._watch.Start();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        t.Tick += (_, _) => { t.Stop(); if (_current == w) w.Close(); };
        t.Start();
    }

    /// <summary>Closes when the user clicks anywhere outside the card.</summary>
    private void CloseOnOutsideClick()
    {
        bool down = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON) || NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);
        if (down && !_mouseWasDown)
        {
            try
            {
                var p = PointFromScreen(NativeMethods.CursorScreenPx());   // takes physical screen pixels
                if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) Close();
            }
            catch { }
        }
        _mouseWasDown = down;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }

    public static void CloseCurrent() => _current?.Close();
}
