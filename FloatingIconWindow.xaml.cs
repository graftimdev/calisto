using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Calisto;

/// <summary>The small draggable planet that floats over everything. Any click opens the menu; it also owns the global hotkey and the passive scan.</summary>
public partial class FloatingIconWindow : Window
{
    private MenuWindow? _menu;
    private HotkeyManager? _hotkeys;
    private readonly PassiveScanner _passive = new();
    private bool _lookingUp;

    // drag state
    private Point _pressScreen;
    private Point _pressOffset;
    private bool _dragging;

    public FloatingIconWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            _hotkeys = new HotkeyManager(source);
            _hotkeys.Pressed += () => _ = LookupSelectionAsync();
            ApplyHotkeyFromSettings();
        };
        Loaded += (_, _) => { PositionInitial(); ApplyOpacity(); ApplyPassiveScan(); };
        Closed += (_, _) => { _hotkeys?.Dispose(); _passive.Dispose(); };
    }

    public void ApplyOpacity()
    {
        Opacity = IsMouseOver ? 1.0 : App.Settings.Opacity;
        _menu?.ApplyOpacity();
    }

    public void ApplyPassiveScan() => _passive.Enabled = App.Settings.PassiveScan;

    private void ApplyHotkeyFromSettings()
    {
        var hk = Hotkey.Parse(App.Settings.Hotkey) ?? Hotkey.Parse("Ctrl+Alt+C")!;
        if (_hotkeys is null) return;
        if (!_hotkeys.TryRegister(hk))
        {
            // Fall back to the default, then to a rarely used alternative.
            foreach (var alt in new[] { "Ctrl+Alt+C", "Ctrl+Alt+D", "Ctrl+Shift+Alt+C" })
            {
                var h = Hotkey.Parse(alt)!;
                if (_hotkeys.TryRegister(h)) { App.Settings.Hotkey = alt; App.Settings.Save(); break; }
            }
        }
    }

    /// <summary>Registers a new combination; false when Windows or another app owns it (the old one stays).</summary>
    public bool TryApplyHotkey(Hotkey hotkey)
    {
        if (_hotkeys is null) return false;
        var previous = _hotkeys.Current;
        if (_hotkeys.TryRegister(hotkey)) return true;
        if (previous is not null) _hotkeys.TryRegister(previous);
        return false;
    }

    /// <summary>Hotkey pressed: read what the user selected in the active app and show its meaning next to it.</summary>
    private async Task LookupSelectionAsync()
    {
        if (_lookingUp) return;
        _lookingUp = true;
        try
        {
            var sel = await SelectionReader.ReadAsync();
            if (sel is null || sel.Text.Length == 0)
            {
                DefinitionWindow.ShowMessage("Select a word first, then press " + App.Settings.Hotkey + ".");
                return;
            }
            var term = sel.Text.Length > 120 ? sel.Text[..120] : sel.Text;
            if (term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 6)
            {
                DefinitionWindow.ShowMessage("Select a single word or a short phrase (up to 6 words).");
                return;
            }
            DefinitionWindow.Show(term, sel.Context, sel.ScreenRect);
        }
        finally { _lookingUp = false; }
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
        ToggleMenu();
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => ToggleMenu();

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

    private void ToggleMenu()
    {
        if (_menu is { IsVisible: true }) { _menu.Hide(); return; }
        EnsureMenu();
        _menu!.ShowNextTo(this);
    }

    private void EnsureMenu() => _menu ??= new MenuWindow(this);

    // ---- hooks used only by UiTest ----
    public void OpenMenuForTest() { EnsureMenu(); _menu!.ShowNextTo(this); }
    public void OpenSettingsForTest() { EnsureMenu(); _menu!.ShowNextTo(this); _menu.ShowSettingsForTest(); }
    public void HideMenuForTest() => _menu?.Hide();
    public Task LookupSelectionForTest() => LookupSelectionAsync();
    public void SetOpacityForTest(double o) { App.Settings.Opacity = o; ApplyOpacity(); Opacity = o; }

    public void ExitApp()
    {
        DefinitionWindow.CloseCurrent();
        _menu?.Close();
        Application.Current.Shutdown();
    }
}
