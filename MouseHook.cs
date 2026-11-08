using System.Runtime.InteropServices;
using System.Windows;

namespace Calisto;

/// <summary>
/// One process-wide low-level mouse hook. Tells the passive scanner when the user scrolls (tags must move) and the
/// definition card when the user clicks somewhere else (card must close). Events are raised on the UI thread.
/// </summary>
public static class MouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static HookProc? _proc;      // kept alive so the delegate is not collected
    private static IntPtr _hook;

    /// <summary>Any mouse button pressed; the point is in physical screen pixels.</summary>
    public static event Action<Point>? ButtonDown;
    /// <summary>Mouse wheel turned (vertical or horizontal).</summary>
    public static event Action? Wheel;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public NativeMethods.POINT pt; public uint mouseData, flags, time; public UIntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public static void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                int msg = wParam.ToInt32();
                if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var p = new Point(info.pt.X, info.pt.Y);
                    Application.Current?.Dispatcher.BeginInvoke(() => ButtonDown?.Invoke(p));
                }
                else if (msg is WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() => Wheel?.Invoke());
                }
            }
            catch { }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
