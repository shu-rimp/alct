using AlctClient.Utils;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AlctClient.Views.Overlays;

public partial class QuickSettingsOverlay : Window
{
    public event Action<string>? LanguageChanged;
    public event Action<bool>? CaptionModeChanged;

    private const double CollapsedSize = 38; // circle(28) + padding(4*2) + border(1*2)

    private static readonly WpfColor BgColor = WpfColor.FromRgb(0x16, 0x14, 0x1F);
    private double _opacity = 0.7;
    private bool _suppressEvents;
    private DispatcherTimer? _collapseTimer;
    private System.Windows.Forms.Screen? _initialScreen;

    public QuickSettingsOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void SetInitialScreen(System.Windows.Forms.Screen screen)
        => _initialScreen = screen;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var screen = _initialScreen ?? System.Windows.Forms.Screen.PrimaryScreen!;
        Left = screen.Bounds.Left + 20;
        Top  = screen.Bounds.Top  + 30;
        ApplyOpacity();
        CollapsePanel();
    }

    private void CollapsePanel()
    {
        ExpandedPanel.Visibility = Visibility.Collapsed;
        SizeToContent = SizeToContent.Manual;
        Width  = CollapsedSize;
        Height = CollapsedSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eMin, uint eMax, IntPtr hmod, WinEventProc fn, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool   UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool   SetWindowPos(IntPtr hwnd, IntPtr hwndAfter, int x, int y, int cx, int cy, uint flags);

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint tid, uint time);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc; // GC 방지용 필드 참조 보존

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndHook);

        // 다른 창이 포그라운드가 될 때마다 topmost 재주장
        // WM_WINDOWPOSCHANGING만으로는 게임이 자기 자신을 topmost로 올릴 때 우리 창이 밀리는 케이스를 잡지 못함
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _winEventProc, 0, 0, 0x0000); // EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT
        Closed += (_, _) => { if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook); };
    }

    private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint tid, uint time)
    {
        if (!IsVisible) return;
        var myHwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == myHwnd) return;
        // SWP_NOSIZE(0x0001) | SWP_NOMOVE(0x0002) | SWP_NOACTIVATE(0x0010)
        SetWindowPos(myHwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0013);
    }

    private IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) // WM_MOUSEACTIVATE
        {
            handled = true;
            return (IntPtr)3; // MA_NOACTIVATE
        }
        if (msg == 0x0046) // WM_WINDOWPOSCHANGING
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & 0x0004) == 0) // SWP_NOZORDER 미설정 = z-order 변경 시도
            {
                pos.hwndInsertAfter = new IntPtr(-1); // HWND_TOPMOST 재고정
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    // ── 호버 확장/축소 ──

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer?.Stop();
        ExpandedPanel.Visibility = Visibility.Visible;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver) CollapsePanel();
        };
        _collapseTimer.Start();
    }

    // ── 투명도 ──

    public void MoveToMonitor(System.Windows.Forms.Screen screen)
    {
        Left = screen.Bounds.Left + 20;
        Top  = screen.Bounds.Top  + 30;
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.1, 1.0);
        if (IsLoaded) ApplyOpacity();
    }

    private void ApplyOpacity() =>
        RootBorder.Background = new WpfBrush(BgColor) { Opacity = _opacity };

    // ── 상태 초기화 ──

    public void SetLanguage(string bcp47)
    {
        _suppressEvents = true;
        BtnJa.IsChecked = bcp47 == "ja-JP";
        BtnZh.IsChecked = bcp47 == "zh-CN";
        BtnEn.IsChecked = bcp47 == "en-US";
        LangLabel.Text = ToShortLabel(bcp47);
        _suppressEvents = false;
    }

    public void SetCaptionMode(bool enabled)
    {
        _suppressEvents = true;
        BtnCaption.IsChecked = enabled;
        _suppressEvents = false;
    }

    private static string ToShortLabel(string bcp47) => bcp47 switch
    {
        "ja-JP" => "JP",
        "zh-CN" => "CN",
        "en-US" => "EN",
        _ => "?"
    };

    // ── 이벤트 핸들러 ──

    private void OnLangChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string tag)
        {
            LangLabel.Text = ToShortLabel(tag);
            LanguageChanged?.Invoke(tag);
        }
    }

    private void OnCaptionToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        CaptionModeChanged?.Invoke(BtnCaption.IsChecked == true);
    }
}
