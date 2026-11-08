using System.Windows;
using System.Windows.Threading;

namespace Calisto;

/// <summary>
/// Quietly scans the active window for abbreviations and keeps the "def" tags in step with it: a new scan when the
/// window changes or moves, a lighter re-scan now and then to follow scrolling and edits. Large pages are skipped.
/// </summary>
public sealed class PassiveScanner : IDisposable
{
    private const int MaxTagsShown = 120;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TagsOverlayWindow _tags = new();
    private IntPtr _hwnd;
    private Rect _bounds;
    private DateTime _lastScan = DateTime.MinValue;
    private TimeSpan _lastDuration = TimeSpan.Zero;
    private bool _scanning;
    private bool _enabled;

    public PassiveScanner()
    {
        _timer.Tick += async (_, _) => await TickAsync();
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
        // Re-scan cadence follows how expensive the last scan was.
        var interval = _lastDuration < TimeSpan.FromMilliseconds(300) ? TimeSpan.FromSeconds(2.5)
                     : _lastDuration < TimeSpan.FromSeconds(1.2) ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(15);
        if (!changed && DateTime.UtcNow - _lastScan < interval) return;
        if (changed) _tags.Clear();

        _hwnd = root; _bounds = bounds;
        _scanning = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hits = await Task.Run(() => ScreenScanner.Scan(root));
            _lastDuration = sw.Elapsed;
            _lastScan = DateTime.UtcNow;

            // Window changed while scanning: results are stale.
            if (NativeMethods.GetForegroundWindow() is var now && now != IntPtr.Zero && !NativeMethods.IsOwnWindow(now) &&
                NativeMethods.GetAncestor(now, NativeMethods.GA_ROOT) != root) return;
            if (hits.Count > MaxTagsShown) hits = hits.Take(MaxTagsShown).ToList();
            _tags.ShowTags(hits);
        }
        catch { }
        finally { _scanning = false; }
    }

    public void Dispose()
    {
        _timer.Stop();
        _tags.Close();
    }
}
