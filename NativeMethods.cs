using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Calisto;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const uint GA_ROOT = 2;

    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_C = 0x43, VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010, SWP_NOZORDER = 0x0004, SWP_SHOWWINDOW = 0x0040, SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Physical-pixel bounds (full monitor and work area) of the monitor that holds a window.</summary>
    public static (Rect Monitor, Rect Work) MonitorBoundsOf(IntPtr hwnd)
    {
        var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return MonitorBounds(mon);
    }

    public static (Rect Monitor, Rect Work) MonitorBoundsAt(Point px)
    {
        var mon = MonitorFromPoint(new POINT { X = (int)px.X, Y = (int)px.Y }, MONITOR_DEFAULTTONEAREST);
        return MonitorBounds(mon);
    }

    private static (Rect Monitor, Rect Work) MonitorBounds(IntPtr mon)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
            return (ToRect(info.rcMonitor), ToRect(info.rcWork));
        var all = new Rect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        return (all, all);
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));

    /// <summary>Scale factor (1.0 at 96 dpi) of the monitor a window is currently on.</summary>
    public static double ScaleOf(IntPtr hwnd)
    {
        try { var dpi = GetDpiForWindow(hwnd); return dpi > 0 ? dpi / 96.0 : 1.0; }
        catch { return 1.0; }
    }

    /// <summary>Moves and sizes a window in physical pixels, bypassing WPF's DIP conversion.</summary>
    public static void MoveWindowPx(Window window, Rect px, bool show)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, HWND_TOPMOST, (int)px.X, (int)px.Y, (int)Math.Ceiling(px.Width), (int)Math.Ceiling(px.Height),
            SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : 0));
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    /// <summary>Adds extended window styles (click-through, no-activate, tool window) to a WPF window.</summary>
    public static void AddExStyle(Window window, int styles)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var current = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(current | (uint)styles));
    }

    /// <summary>True when the window (any popup, menu or tooltip included) belongs to this process.</summary>
    public static bool IsOwnWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static Rect GetWindowBounds(IntPtr hwnd)
    {
        if (GetWindowRect(hwnd, out var r))
            return new Rect(r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        return Rect.Empty;
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static Point CursorScreenPx()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Taps a key (down and up) as if pressed on the keyboard.</summary>
    public static void TapKey(int vk)
    {
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void KeyUp(int vk) => keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    public static void KeyDown(int vk) => keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
}
