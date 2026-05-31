using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AlctClient.Utils;

public static class WindowsApiHelper
{
    private const int GWL_EX_STYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int HWND_TOPMOST = -1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_C = 0x43;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static void SimulateCopy() => SendCtrlKey(VK_C);
    public static void SimulatePaste() => SendCtrlKey(VK_V);

    private static void SendCtrlKey(ushort vk)
    {
        var inputs = new[]
        {
            KeyDown(VK_CONTROL),
            KeyDown(vk),
            KeyUp(vk),
            KeyUp(VK_CONTROL),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyDown(ushort vk) =>
        new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } };

    private static INPUT KeyUp(ushort vk) =>
        new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } };

    public static void EnableClickThrough(Window window)
    {
        var hwnd = GetWindowHandle(window);
        var style = GetWindowLong(hwnd, GWL_EX_STYLE);
        SetWindowLong(hwnd, GWL_EX_STYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    public static void DisableClickThrough(Window window)
    {
        var hwnd = GetWindowHandle(window);
        var style = GetWindowLong(hwnd, GWL_EX_STYLE);
        SetWindowLong(hwnd, GWL_EX_STYLE, style & ~WS_EX_TRANSPARENT);
    }

    public static void PinToTopmost(Window window)
    {
        var hwnd = GetWindowHandle(window);
        SetWindowPos(hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    private static IntPtr GetWindowHandle(Window window) =>
        new WindowInteropHelper(window).Handle;
}
