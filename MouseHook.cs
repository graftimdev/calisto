using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Calisto;

/// <summary>
/// One process-wide low-level mouse hook. Tells the passive scanner when the user scrolls (tags must move) and the
/// definition card when the user clicks somewhere else (card must close). Events are raised on the UI thread.
/// The hook lives on its own thread with its own message loop: Windows silently removes a low-level hook whose
/// thread does not answer within ~300 ms, and the UI thread is busy for longer than that while it reads a window
/// through UI Automation.
/// </summary>
public static class MouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
    private const uint WM_QUIT = 0x0012;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static HookProc? _proc;      // kept alive so the delegate is not collected
    private static IntPtr _hook;
    private static Thread? _thread;
    private static uint _threadId;

    /// <summary>Any mouse button pressed; the point is in physical screen pixels.</summary>
    public static event Action<Point>? ButtonDown;
    /// <summary>Mouse wheel turned (vertical or horizontal).</summary>
    public static event Action? Wheel;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public NativeMethods.POINT pt; public uint mouseData, flags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public NativeMethods.POINT pt; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    public static void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "Calisto mouse hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public static void Stop()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread = null;
    }

    private static void HookThread()
    {
        _threadId = GetCurrentThreadId();
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
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
