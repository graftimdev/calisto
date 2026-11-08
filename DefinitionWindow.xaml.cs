using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Calisto;

/// <summary>Small card that shows one word and its definition next to where the word is on screen. Positioned in physical pixels.</summary>
public partial class DefinitionWindow : Window
{
    private static DefinitionWindow? _current;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _mouseWasDown;
    private Rect? _anchorPx;
    private DateTime _shownAt;

    /// <summary>The click or key press that opened the card must never count as a click outside it.</summary>
    private bool InGracePeriod => (DateTime.UtcNow - _shownAt).TotalMilliseconds < 350;

    private DefinitionWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW);
        _watch.Tick += (_, _) => CloseOnOutsideClick();
        MouseHook.ButtonDown += OnGlobalButtonDown;
        Closed += (_, _) => { _watch.Stop(); MouseHook.ButtonDown -= OnGlobalButtonDown; if (_current == this) _current = null; };
    }

    /// <summary>A click anywhere outside the card closes it (global hook, so even very quick clicks count).</summary>
    private void OnGlobalButtonDown(Point screenPx)
    {
        if (!IsVisible || InGracePeriod) return;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var mine = NativeMethods.GetWindowBounds(hwnd);
            if (!mine.Contains(screenPx)) Close();
        }
        catch { }
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
        w._anchorPx = anchorPx is { Width: > 0 } r ? r : null;
        w.Show();
        w.Place();
        w.StartWatching();
        w.LookupAsync(word, context);
        return w;
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
        w.Place();
        w.StartWatching();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        t.Tick += (_, _) => { t.Stop(); if (_current == w) w.Close(); };
        t.Start();
    }

    private async void LookupAsync(string word, string context)
    {
        try
        {
            var result = await AbbreviationLookup.LookupAsync(word, context);
            if (_current != this) return;
            Body.Text = result.Text;
            Source.Text = result.Source;
            Place();
        }
        catch (Exception ex)
        {
            if (_current != this) return;
            Body.Text = "Lookup failed: " + ex.Message;
        }
    }

    /// <summary>Puts the card just below the anchor (or the mouse), keeping it inside that monitor's work area.</summary>
    private void Place()
    {
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        Point anchorTopLeft, anchorBottomLeft;
        if (_anchorPx is { } a) { anchorTopLeft = a.TopLeft; anchorBottomLeft = a.BottomLeft; }
        else
        {
            var m = NativeMethods.CursorScreenPx();
            anchorTopLeft = new Point(m.X, m.Y - 8);
            anchorBottomLeft = new Point(m.X, m.Y + 16);
        }

        var (_, work) = NativeMethods.MonitorBoundsAt(anchorBottomLeft);
        double scale = NativeMethods.ScaleOf(hwnd);
        double w = Math.Max(ActualWidth, Width) * scale;
        double h = Math.Max(ActualHeight, 60) * scale;

        double x = anchorBottomLeft.X;
        double y = anchorBottomLeft.Y + 6;
        if (x + w > work.Right - 4) x = Math.Max(work.Left + 4, work.Right - w - 4);
        if (x < work.Left + 4) x = work.Left + 4;
        if (y + h > work.Bottom - 4)
        {
            y = anchorTopLeft.Y - h - 6;                      // above the word instead
            if (y < work.Top + 4) y = Math.Max(work.Top + 4, work.Bottom - h - 4);
        }
        NativeMethods.MoveWindowPx(this, new Rect(x, y, w, h), show: true);
    }

    private void StartWatching()
    {
        _shownAt = DateTime.UtcNow;
        // Seed with the current state so a button still held from the click that opened the card is not seen as a new press.
        _mouseWasDown = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON) || NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);
        _watch.Start();
    }

    /// <summary>Fallback when the hook is unavailable: closes when the user clicks anywhere outside the card.</summary>
    private void CloseOnOutsideClick()
    {
        // GetAsyncKeyState bit 0 means "pressed since the last call", so quick clicks between polls are not missed.
        // During the grace period the calls only clear that bit (it was set by the click that opened the card).
        bool down = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8001) != 0 | (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8001) != 0;
        if (InGracePeriod) { _mouseWasDown = true; return; }
        if (down && !_mouseWasDown)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var mine = NativeMethods.GetWindowBounds(hwnd);
                if (!mine.Contains(NativeMethods.CursorScreenPx())) Close();
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
