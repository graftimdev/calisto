using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Calisto;

/// <summary>The small draggable planet that floats over everything. Click opens the menu, drag moves it.</summary>
public partial class FloatingIconWindow : Window
{
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IntPtr _lastForeignWindow = IntPtr.Zero;
    private MenuWindow? _menu;
    private bool _scanning;

    // drag state
    private Point _pressScreen;
    private Point _pressOffset;
    private bool _dragging;

    public FloatingIconWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW);
        Loaded += (_, _) => { PositionInitial(); ApplyOpacity(); };
        _foregroundTimer.Tick += (_, _) => RememberForegroundWindow();
        _foregroundTimer.Start();
    }

    public void ApplyOpacity()
    {
        Opacity = IsMouseOver ? 1.0 : App.Settings.Opacity;
        _menu?.ApplyOpacity();
    }

    private void PositionInitial()
    {
        var area = SystemParameters.WorkArea;
        var s = App.Settings;
        if (s.IconLeft is double l && s.IconTop is double t &&
            l >= area.Left - 10 && l <= area.Right - Width + 10 && t >= area.Top - 10 && t <= area.Bottom - Height + 10)
        {
            Left = l; Top = t;
        }
        else
        {
            // Right edge, two thirds of the way down the screen.
            Left = area.Right - Width - 4;
            Top = area.Top + area.Height * 2 / 3 - Height / 2;
        }
    }

    /// <summary>Remembers the last window that had focus and does not belong to Calisto.</summary>
    private void RememberForegroundWindow()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || NativeMethods.IsOwnWindow(fg)) return;
        var root = NativeMethods.GetAncestor(fg, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero) root = fg;
        if (NativeMethods.IsWindowVisible(root)) _lastForeignWindow = root;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e) => Opacity = 1.0;
    private void Window_MouseLeave(object sender, MouseEventArgs e) { if (!_dragging) Opacity = App.Settings.Opacity; }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressScreen = PointToScreen(e.GetPosition(this));
        _pressOffset = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var now = PointToScreen(e.GetPosition(this));
        if (!_dragging && (Math.Abs(now.X - _pressScreen.X) > 4 || Math.Abs(now.Y - _pressScreen.Y) > 4))
            _dragging = true;
        if (_dragging)
        {
            var dip = ScreenToDip(now);
            Left = dip.X - _pressOffset.X;
            Top = dip.Y - _pressOffset.Y;
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        if (_dragging)
        {
            _dragging = false;
            ClampToScreen();
            App.Settings.IconLeft = Left;
            App.Settings.IconTop = Top;
            App.Settings.Save();
            return;
        }
        // Left click: scan right away.
        _ = ScanAsync();
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Right click: open (or hide) the menu.
        ToggleMenu();
    }

    private Point ScreenToDip(Point screenPx)
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget is { } ct ? ct.TransformFromDevice.Transform(screenPx) : screenPx;
    }

    private void ClampToScreen()
    {
        var a = SystemParameters.VirtualScreenLeft; var b = SystemParameters.VirtualScreenTop;
        Left = Math.Clamp(Left, a, a + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(Top, b, b + SystemParameters.VirtualScreenHeight - Height);
    }

    public Point IconCenterDip => new(Left + Width / 2, Top + Height / 2);

    private void ToggleMenu()
    {
        if (_menu is { IsVisible: true }) { _menu.Hide(); return; }
        EnsureMenu();
        _menu!.ShowNextTo(this);
    }

    private void EnsureMenu()
    {
        if (_menu is not null) return;
        _menu = new MenuWindow(this);
        _menu.ScanRequested += async () => await ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (_scanning) return;
        var target = _lastForeignWindow;
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target))
        {
            _menu?.ShowMessage("Click into the window you want to scan first, then press Scan.");
            return;
        }

        _scanning = true;
        _menu?.Hide();
        var overlay = new ScanOverlayWindow(IconCenterDip);
        overlay.Show();
        try
        {
            var scanTask = Task.Run(async () =>
            {
                var hits = ScreenScanner.Scan(target);
                if (hits.Count == 0)
                {
                    // Browsers switch accessibility on when first asked; give them a moment and try once more.
                    await Task.Delay(800);
                    hits = ScreenScanner.Scan(target);
                }
                return hits;
            });
            await Task.WhenAll(scanTask, Task.Delay(1500));
            var hits = await scanTask;
            overlay.Close();
            EnsureMenu();
            _menu!.ShowResults(hits, NativeMethods.GetWindowTitle(target));
            _menu.ShowNextTo(this);
        }
        catch (Exception ex)
        {
            overlay.Close();
            EnsureMenu();
            _menu!.ShowMessage("Scan failed: " + ex.Message);
            _menu.ShowNextTo(this);
        }
        finally { _scanning = false; }
    }

    // ---- hooks used only by UiTest ----
    public void OpenMenuForTest() { EnsureMenu(); _menu!.ShowNextTo(this); }
    public Task ScanForTest(IntPtr target) { _lastForeignWindow = target; return ScanAsync(); }
    public int ResultCountForTest => _menu?.ResultCount ?? -1;
    public Task ExpandFirstResultForTest() => _menu?.ExpandFirstForTest() ?? Task.CompletedTask;
    public void SetOpacityForTest(double o) { App.Settings.Opacity = o; _menu?.SetSliderForTest(o); ApplyOpacity(); Opacity = o; }

    public void ExitApp()
    {
        _menu?.Close();
        Application.Current.Shutdown();
    }
}
