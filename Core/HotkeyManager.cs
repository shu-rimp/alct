using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AlctClient.Core;

public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;
    private const int INPUT_TRANSLATION_HOTKEY_ID = 9001;
    private const int COOLDOWN_MS = 1000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private DateTime _lastTriggerTime = DateTime.MinValue;
    private bool _disposed;

    public event Action? HotkeyPressed;
    public event Action? InputTranslationHotkeyPressed;

    public HotkeyManager(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    public bool Register(uint modifiers, uint virtualKey) =>
        RegisterHotKey(_hwnd, HOTKEY_ID, modifiers, virtualKey);

    public bool RegisterInputTranslation(uint modifiers, uint virtualKey) =>
        RegisterHotKey(_hwnd, INPUT_TRANSLATION_HOTKEY_ID, modifiers, virtualKey);

    public void Unregister()
    {
        UnregisterHotKey(_hwnd, HOTKEY_ID);
        UnregisterHotKey(_hwnd, INPUT_TRANSLATION_HOTKEY_ID);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if ((id == HOTKEY_ID || id == INPUT_TRANSLATION_HOTKEY_ID) && IsCooldownElapsed())
            {
                _lastTriggerTime = DateTime.UtcNow;
                if (id == HOTKEY_ID) HotkeyPressed?.Invoke();
                else InputTranslationHotkeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public bool IsCooldownElapsed() =>
        (DateTime.UtcNow - _lastTriggerTime).TotalMilliseconds >= COOLDOWN_MS;

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _disposed = true;
    }
}

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}
