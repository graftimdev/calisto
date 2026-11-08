using System.Windows;
using System.Windows.Threading;

namespace Calisto;

/// <summary>
/// Quietly scans the active window for abbreviations and keeps the "def" tags in step with it: a new scan when the
/// window changes, moves or is scrolled, and a light re-scan now and then to follow edits. Large pages are capped.
/// </summary>
public sealed class PassiveScanner : IDisposable
{
    private const int MaxTagsShown = 120;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TagsOverlayWindow _tags = new();
    private IntPtr _hwnd;
    private Rect _bounds;
    private DateTime _lastScan = DateTime.MinValue;
    private DateTime _rescanAt = DateTime.MaxValue;
    private TimeSpan _lastDuration = TimeSpan.Zero;
    private bool _scanning;
    private bool _enabled;

    public PassiveScanner()
    {
        _timer.Tick += async (_, _) => await TickAsync();
        MouseHook.Wheel += OnWheel;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (value) _timer.Start();
            else { _timer.Stop(); _tags.Clear(); _hwnd = IntPtr.Zero; }
        }
    }

    /// <summary>Scrolling moves the text: hide the tags at once and re-scan shortly after the wheel stops.</summary>
    private void OnWheel()
    {
        if (!_enabled) return;
        _tags.HideTemporarily();
        _rescanAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(450);
    }

    private async Task TickAsync()
    {
        if (_scanning) return;
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || NativeMethods.IsOwnWindow(fg)) { _tags.HideTemporarily(); return; }   // our own menu or card is in front
        var root = NativeMethods.GetAncestor(fg, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero) root = fg;
        if (!NativeMethods.IsWindowVisible(root) || NativeMethods.IsIconic(root)) { _tags.Clear(); _hwnd = IntPtr.Zero; return; }

        var bounds = NativeMethods.GetWindowBounds(root);
        bool changed = root != _hwnd || bounds != _bounds;
        bool scrolled = DateTime.UtcNow >= _rescanAt;
        // Re-scan cadence follows how expensive the last scan was.
        var interval = _lastDuration < TimeSpan.FromMilliseconds(300) ? TimeSpan.FromSeconds(2)
                     : _lastDuration < TimeSpan.FromSeconds(1.2) ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(8);
        if (!changed && !scrolled && DateTime.UtcNow - _lastScan < interval) return;
        if (changed) _tags.Clear();
        if (scrolled && _rescanAt != DateTime.MaxValue && DateTime.UtcNow - _rescanAt > TimeSpan.FromSeconds(5)) _rescanAt = DateTime.MaxValue;

        _hwnd = root; _bounds = bounds;
        _scanning = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hits = await Task.Run(() => ScreenScanner.Scan(root, TimeSpan.FromSeconds(3)));
            _lastDuration = sw.Elapsed;
            _lastScan = DateTime.UtcNow;
            if (scrolled) _rescanAt = DateTime.MaxValue;

            // Window changed while scanning: results are stale.
            var now = NativeMethods.GetForegroundWindow();
            if (now != IntPtr.Zero && !NativeMethods.IsOwnWindow(now) && NativeMethods.GetAncestor(now, NativeMethods.GA_ROOT) != root) return;
            if (!_enabled) return;
            if (hits.Count > MaxTagsShown) hits = hits.Take(MaxTagsShown).ToList();
            _tags.ShowTags(hits, root);
        }
        catch (Exception ex) { App.LogCrash("passive scan", ex); }
        finally { _scanning = false; }
    }

    public void Dispose()
    {
        MouseHook.Wheel -= OnWheel;
        _timer.Stop();
        _tags.Close();
    }
}
