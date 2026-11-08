using System.Windows.Input;
using System.Windows.Interop;

namespace Calisto;

/// <summary>A keyboard combination such as Ctrl+Alt+C, with parsing, formatting and validation.</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (k - Key.NumPad0),
        Key.OemComma => ",", Key.OemPeriod => ".", Key.OemMinus => "-", Key.OemPlus => "=", Key.OemQuestion => "/",
        Key.OemSemicolon => ";", Key.OemQuotes => "'", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemTilde => "`",
        Key.OemBackslash or Key.OemPipe => "\\", Key.Return => "Enter", Key.Space => "Space",
        _ => k.ToString()
    };

    public static Hotkey? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (raw.Length == 1 && char.IsLetter(raw[0])) key = Enum.Parse<Key>(raw.ToUpperInvariant());
                    else if (raw.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw[3..], out var n)) key = Key.NumPad0 + n;
                    else if (Enum.TryParse<Key>(raw, true, out var k)) key = k;
                    break;
            }
        }
        return key == Key.None ? null : new Hotkey(mods, key);
    }

    /// <summary>Why a combination is not usable, or null when it is fine.</summary>
    public static string? Problem(ModifierKeys mods, Key key)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return "Add a normal key to the modifiers, for example Ctrl+Alt+C.";
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            return "The combination needs Ctrl, Alt or Win, otherwise it would fire while typing.";
        if (mods == ModifierKeys.Control && key is Key.C or Key.V or Key.X or Key.Z or Key.A or Key.S)
            return $"Ctrl+{KeyName(key)} is used by every app for editing. Add Alt or Shift.";
        if (mods == ModifierKeys.Alt && key is Key.F4 or Key.Tab)
            return "That combination belongs to Windows.";
        return null;
    }

    public uint NativeModifiers
    {
        get
        {
            uint m = NativeMethods.MOD_NOREPEAT;
            if (Modifiers.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= NativeMethods.MOD_WIN;
            return m;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);
}

/// <summary>Registers one global hotkey on a window and raises Pressed when the user hits it.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int Id = 0xCA15;
    private readonly HwndSource _source;
    public Hotkey? Current { get; private set; }
    public event Action? Pressed;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(Hook);
    }

    /// <summary>Tries to register the combination; returns false when Windows or another app already owns it.</summary>
    public bool TryRegister(Hotkey hotkey)
    {
        Unregister();
        if (!NativeMethods.RegisterHotKey(_source.Handle, Id, hotkey.NativeModifiers, hotkey.VirtualKey)) return false;
        Current = hotkey;
        return true;
    }

    /// <summary>Checks whether a combination could be registered without keeping it.</summary>
    public bool CanRegister(Hotkey hotkey)
    {
        if (Current == hotkey) return true;
        const int probe = 0xCA16;
        bool ok = NativeMethods.RegisterHotKey(_source.Handle, probe, hotkey.NativeModifiers, hotkey.VirtualKey);
        if (ok) NativeMethods.UnregisterHotKey(_source.Handle, probe);
        return ok;
    }

    public void Unregister()
    {
        if (Current is null) return;
        NativeMethods.UnregisterHotKey(_source.Handle, Id);
        Current = null;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
    }
}
