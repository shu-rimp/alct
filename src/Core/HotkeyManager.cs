using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AlctClient.Core;

public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int HOTKEY_ID = 9000;
    private const int INPUT_TRANSLATION_HOTKEY_ID = 9001;
    private const int DISMISS_HOTKEY_ID = 9002;
    private const uint VK_ESCAPE = 0x1B;
    private const uint MOD_NOREPEAT = 0x4000;  // 키를 누르고 있어도 WM_HOTKEY가 반복 발생하지 않게 — 길게 누르기 판별용
    private const int COOLDOWN_MS = 1000;
    private const int LONG_PRESS_MS = 500;     // 이 시간 이상 누르고 있으면 "길게 누르기"(드래그 영역 선택)
    private const int PRESS_POLL_MS = 40;       // 누름 유지 여부 폴링 간격

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    private readonly IntPtr _hwnd;
    private DateTime _lastTriggerTime = DateTime.MinValue;
    private uint _captureVKey;          // 길게 누르기 판별을 위한 채팅 캡처 키 — 등록 시 보관
    private DispatcherTimer? _pressTimer;
    private bool _disposed;

    public event Action? HotkeyPressed;
    public event Action? InputTranslationHotkeyPressed;
    public event Action? ClipboardUpdated;  // 사용자가 무언가 복사함 — 입력창 번역 "준비 완료" 힌트용(키 주입 아님, 수동 알림)
    public event Action? DismissHotkeyPressed;  // ESC — 번역 오버레이 숨김. 오버레이가 보일 때만 등록(평소 게임의 ESC를 막지 않음)
    public event Action? LongPressHotkeyPressed;  // 채팅 캡처 키 길게 누르기 — 드래그로 영역 선택(1회성)

    public HotkeyManager(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        AddClipboardFormatListener(_hwnd);
    }

    // MOD_NOREPEAT — 키를 누르고 있어도 WM_HOTKEY는 1회만. 길게 누르기는 GetAsyncKeyState 폴링으로 판별.
    public bool Register(uint modifiers, uint virtualKey)
    {
        _captureVKey = virtualKey;
        return RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, virtualKey);
    }

    public bool RegisterInputTranslation(uint modifiers, uint virtualKey) =>
        RegisterHotKey(_hwnd, INPUT_TRANSLATION_HOTKEY_ID, modifiers, virtualKey);

    public bool Reregister(uint modifiers, uint virtualKey)
    {
        _captureVKey = virtualKey;
        UnregisterHotKey(_hwnd, HOTKEY_ID);
        return RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, virtualKey);
    }

    public bool ReregisterInputTranslation(uint modifiers, uint virtualKey)
    {
        UnregisterHotKey(_hwnd, INPUT_TRANSLATION_HOTKEY_ID);
        return RegisterHotKey(_hwnd, INPUT_TRANSLATION_HOTKEY_ID, modifiers, virtualKey);
    }

    // 번역 오버레이가 보이는 동안만 ESC를 가로채 숨김에 쓴다. 보이지 않을 땐 해제해 게임의 ESC를 막지 않는다.
    public bool RegisterDismiss() => RegisterHotKey(_hwnd, DISMISS_HOTKEY_ID, 0, VK_ESCAPE);
    public void UnregisterDismiss() => UnregisterHotKey(_hwnd, DISMISS_HOTKEY_ID);

    public void Unregister()
    {
        UnregisterHotKey(_hwnd, HOTKEY_ID);
        UnregisterHotKey(_hwnd, INPUT_TRANSLATION_HOTKEY_ID);
        UnregisterHotKey(_hwnd, DISMISS_HOTKEY_ID);
    }

    public static string FormatHotkey(uint modifiers, uint vkey)
    {
        var parts = new List<string>();
        if ((modifiers & (uint)HotkeyModifiers.Ctrl)  != 0) parts.Add("Ctrl");
        if ((modifiers & (uint)HotkeyModifiers.Alt)   != 0) parts.Add("Alt");
        if ((modifiers & (uint)HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & (uint)HotkeyModifiers.Win)   != 0) parts.Add("Win");
        if (vkey != 0) parts.Add(VkeyToDisplayString(vkey));
        return string.Join(" + ", parts);
    }

    private static string VkeyToDisplayString(uint vkey)
    {
        var key = (System.Windows.Forms.Keys)vkey;
        var str = key.ToString();
        // "D0"~"D9" → "0"~"9"
        if (str.Length == 2 && str[0] == 'D' && char.IsDigit(str[1]))
            return str[1..];
        return str;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == DISMISS_HOTKEY_ID)  // ESC — 쿨다운과 무관하게 즉시 숨김
            {
                DismissHotkeyPressed?.Invoke();
                handled = true;
            }
            else if ((id == HOTKEY_ID || id == INPUT_TRANSLATION_HOTKEY_ID) && IsCooldownElapsed())
            {
                _lastTriggerTime = DateTime.UtcNow;
                if (id == HOTKEY_ID) BeginCapturePress();   // 짧게=일반 캡처 / 길게=드래그 영역 선택
                else InputTranslationHotkeyPressed?.Invoke();
                handled = true;
            }
        }
        else if (msg == WM_CLIPBOARDUPDATE)
        {
            ClipboardUpdated?.Invoke();
        }
        return IntPtr.Zero;
    }

    public bool IsCooldownElapsed() =>
        (DateTime.UtcNow - _lastTriggerTime).TotalMilliseconds >= COOLDOWN_MS;

    // 캡처 키가 눌린 직후 호출. 키 유지 여부를 폴링해 짧게 누름(일반 캡처) vs 길게 누름(드래그 영역 선택)을 구분.
    // MOD_NOREPEAT로 WM_HOTKEY는 1회만 오므로, 실제 키 상태는 GetAsyncKeyState로 직접 본다.
    private void BeginCapturePress()
    {
        _pressTimer?.Stop();
        var start = DateTime.UtcNow;
        _pressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PRESS_POLL_MS) };
        _pressTimer.Tick += (_, _) =>
        {
            var heldMs = (DateTime.UtcNow - start).TotalMilliseconds;
            bool stillDown = (GetAsyncKeyState((int)_captureVKey) & 0x8000) != 0;

            // 키를 이미 뗐으면 무조건 일반 캡처. stillDown을 반드시 먼저 검사 —
            // DispatcherTimer가 밀려 첫 틱이 임계(500ms)를 넘겨 도착해도, 짧게 누름이
            // 길게 누름(드래그 선택)으로 오인되지 않게 한다. (오인 시 FPS에서 포커스를 빼앗아
            // 조준 커서가 중앙에 고정되는 문제로 이어짐)
            if (!stillDown)                     // 떼면 일반 캡처
            {
                _pressTimer!.Stop();
                HotkeyPressed?.Invoke();
            }
            else if (heldMs >= LONG_PRESS_MS)   // 여전히 누르고 있고 임계 도달 — 드래그 영역 선택 진입
            {
                _pressTimer!.Stop();
                LongPressHotkeyPressed?.Invoke();
            }
        };
        _pressTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _pressTimer?.Stop();
        Unregister();
        RemoveClipboardFormatListener(_hwnd);
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
