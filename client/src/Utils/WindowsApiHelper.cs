using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;
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
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOZORDER   = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private static System.Drawing.Point? _liveCaptionsOriginalPos;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LWIN    = 0x5B;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_C = 0x43;
    private const ushort VK_L = 0x4C;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static IntPtr FindLiveCaptionsHandle(bool requireVisible = true)
    {
        var processes = Process.GetProcessesByName("LiveCaptions");
        if (processes.Length == 0) return IntPtr.Zero;

        var targetPid = (uint)processes[0].Id;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != targetPid) return true;
            if (requireVisible && !IsWindowVisible(hwnd)) return true;
            GetWindowRect(hwnd, out RECT rect);
            if (rect.Right - rect.Left > 0 && rect.Bottom - rect.Top > 0)
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static void SetLiveCaptionsVisible(bool visible)
    {
        if (visible)
        {
            var hwnd = FindLiveCaptionsHandle(requireVisible: false);
            if (hwnd == IntPtr.Zero) return;
            var pos = _liveCaptionsOriginalPos ?? new System.Drawing.Point(0, 0);
            SetWindowPos(hwnd, IntPtr.Zero, pos.X, pos.Y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            _liveCaptionsOriginalPos = null;
        }
        else
        {
            var hwnd = FindLiveCaptionsHandle(requireVisible: true);
            if (hwnd == IntPtr.Zero) return;
            GetWindowRect(hwnd, out RECT rect);
            if (rect.Left > -1000 && rect.Top > -1000)
                _liveCaptionsOriginalPos = new System.Drawing.Point(rect.Left, rect.Top);
            SetWindowPos(hwnd, IntPtr.Zero, -10000, -10000, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    public static Rectangle? GetLiveCaptionsRegion()
    {
        var hwnd = FindLiveCaptionsHandle();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowRect(hwnd, out RECT rect);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        return w > 0 && h > 0 ? new Rectangle(rect.Left, rect.Top, w, h) : null;
    }

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

    public static async Task StartLiveCaptionsAsync()
    {
        if (Process.GetProcessesByName("LiveCaptions").Length > 0) return;
        var inputs = new[]
        {
            KeyDown(VK_LWIN), KeyDown(VK_CONTROL), KeyDown(VK_L),
            KeyUp(VK_L), KeyUp(VK_CONTROL), KeyUp(VK_LWIN),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            if (Process.GetProcessesByName("LiveCaptions").Length > 0) break;
        }
    }

    public static async Task WaitForLiveCaptionsWindowAsync(int timeoutMs = 5000)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            if (FindLiveCaptionsHandle(requireVisible: true) != IntPtr.Zero) return;
            await Task.Delay(200);
        }
    }

    public static void StopLiveCaptions()
    {
        foreach (var p in Process.GetProcessesByName("LiveCaptions"))
        {
            try { p.Kill(entireProcessTree: true); }
            catch { try { p.Kill(); } catch { } }
            finally { p.Dispose(); }
        }
    }

    public static async Task StopLiveCaptionsAsync()
    {
        foreach (var p in Process.GetProcessesByName("LiveCaptions"))
        {
            try { p.Kill(entireProcessTree: true); }
            catch { try { p.Kill(); } catch { } }
            finally { p.Dispose(); }
        }
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(200);
            if (Process.GetProcessesByName("LiveCaptions").Length == 0) break;
        }
        await Task.Delay(500); // Windows 내부 정리 대기
    }

    public static void SetLiveCaptionsLanguage(string bcp47Tag)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\LiveCaptions\UI", writable: true);
        key?.SetValue("CaptionLanguage", bcp47Tag, RegistryValueKind.String);
    }

    public static bool IsLiveCaptionSupported()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key?.GetValue("CurrentBuild") is not string build) return false;
        return int.TryParse(build, out int n) && n >= 22621;
    }
}
