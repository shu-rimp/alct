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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

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
