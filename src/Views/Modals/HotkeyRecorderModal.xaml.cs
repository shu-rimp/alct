using AlctClient.Core;
using System.Windows;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AlctClient.Views.Modals;

public partial class HotkeyRecorderModal : Window
{
    public uint Modifiers { get; private set; }
    public uint VKey      { get; private set; }

    private bool _liveMode = true;
    private readonly uint _otherModifiers;
    private readonly uint _otherVKey;

    private static readonly HashSet<(uint mods, uint vkey)> BlockedCombos = new()
    {
        ((uint)HotkeyModifiers.Ctrl, 0x41), // Ctrl+A
        ((uint)HotkeyModifiers.Ctrl, 0x43), // Ctrl+C
        ((uint)HotkeyModifiers.Ctrl, 0x53), // Ctrl+S
        ((uint)HotkeyModifiers.Ctrl, 0x56), // Ctrl+V
        ((uint)HotkeyModifiers.Ctrl, 0x58), // Ctrl+X
        ((uint)HotkeyModifiers.Ctrl, 0x5A), // Ctrl+Z
        ((uint)HotkeyModifiers.Alt,  0x73), // Alt+F4
    };

    public HotkeyRecorderModal(string title, uint currentModifiers, uint currentVKey, uint otherModifiers, uint otherVKey)
    {
        InitializeComponent();
        Modifiers = currentModifiers;
        VKey = currentVKey;
        _otherModifiers = otherModifiers;
        _otherVKey = otherVKey;
        SubtitleText.Text = $"{title} 단축키를 입력하세요.";
        HotkeyDisplay.Text = HotkeyManager.FormatHotkey(currentModifiers, currentVKey);
        SetHint(false, null);
    }

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierKey(key))
        {
            e.Handled = true;
            _liveMode = true;
            UpdateLiveDisplay();
            return;
        }

        var modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.None)
        {
            if (key == Key.Escape) { e.Handled = true; OnCancel(this, new RoutedEventArgs()); return; }
            if (key == Key.Enter && ConfirmButton.IsEnabled) { e.Handled = true; OnConfirm(this, new RoutedEventArgs()); return; }

            if (IsFunctionKey(key) || IsAlphanumericKey(key))
            {
                e.Handled = true;
                RecordCombo(ModifierKeys.None, key);
                return;
            }

            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        RecordCombo(modifiers, key);
    }

    protected override void OnPreviewKeyUp(WpfKeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_liveMode && IsModifierKey(key))
        {
            e.Handled = true;
            UpdateLiveDisplay();
            return;
        }
        base.OnPreviewKeyUp(e);
    }

    private void UpdateLiveDisplay()
    {
        var mods = ToHotkeyModifiers(Keyboard.Modifiers);
        HotkeyDisplay.Text = mods != 0 ? HotkeyManager.FormatHotkey(mods, 0) : string.Empty;
    }

    private void RecordCombo(ModifierKeys modifiers, Key key)
    {
        var mods = ToHotkeyModifiers(modifiers);
        var vkey = (uint)KeyInterop.VirtualKeyFromKey(key);
        _liveMode = false;
        HotkeyDisplay.Text = HotkeyManager.FormatHotkey(mods, vkey);

        if (mods == 0 && IsAlphanumericKey(key))
        {
            ConfirmButton.IsEnabled = false;
            SetHint(true, "알파벳/숫자 키는 조합키(Ctrl / Alt / Shift)와 함께 눌러 주세요.");
            return;
        }

        if (BlockedCombos.Contains((mods, vkey)))
        {
            ConfirmButton.IsEnabled = false;
            SetHint(true, "시스템 기본 단축키(복사/붙여넣기 등)와 충돌해요. 다른 조합을 선택해 주세요.");
            return;
        }

        if (mods == _otherModifiers && vkey == _otherVKey)
        {
            ConfirmButton.IsEnabled = false;
            SetHint(true, $"다른 단축키({HotkeyManager.FormatHotkey(_otherModifiers, _otherVKey)})와 중복돼요. 다른 조합을 선택해 주세요.");
            return;
        }

        Modifiers = mods;
        VKey = vkey;
        ConfirmButton.IsEnabled = true;
        SetHint(false, null);
    }

    private static uint ToHotkeyModifiers(ModifierKeys modifiers)
    {
        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= (uint)HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(ModifierKeys.Alt))     mods |= (uint)HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift))   mods |= (uint)HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= (uint)HotkeyModifiers.Win;
        return mods;
    }

    private void SetHint(bool isError, string? message)
    {
        HintText.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("TextDangerBrush")
            : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        HintText.Text = message ?? "조합키(Ctrl / Alt / Shift)를 함께 누르거나, F1~F24 단독으로 설정할 수 있어요.";
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or
               Key.LeftAlt  or Key.RightAlt  or
               Key.LeftShift or Key.RightShift or
               Key.LWin or Key.RWin;

    private static bool IsFunctionKey(Key key) =>
        key >= Key.F1 && key <= Key.F24;

    private static bool IsAlphanumericKey(Key key) =>
        (key >= Key.A && key <= Key.Z) ||
        (key >= Key.D0 && key <= Key.D9) ||
        (key >= Key.NumPad0 && key <= Key.NumPad9);

    private bool _allowClose;

    // Alt+F4 등 시스템 닫기를 차단 — 확인/취소(Esc 포함)로만 닫히게 한다
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; return; }
        base.OnClosing(e);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) { _allowClose = true; DialogResult = true;  Close(); }
    private void OnCancel(object sender, RoutedEventArgs e)  { _allowClose = true; DialogResult = false; Close(); }
}
