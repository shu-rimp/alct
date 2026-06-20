using AlctClient.Utils;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AlctClient.Views.Overlays;

// 입력창 번역 전용 피드백 오버레이.
// 채팅 오버레이(_overlay)를 재사용하면 박스가 커서 거슬리므로, 작은 알약형으로 분리했다.
// 상태: 번역 중(스피너, 자동 숨김 없음) → 완료(초록 점 + 결과, 자동 숨김) / 안내(주황 점, 자동 숨김).
public partial class InputTranslationOverlay : Window
{
    private const int AUTO_HIDE_DELAY_MS = 4000;
    private const int MAX_RESULT_CHARS = 10;  // 결과 미리보기 최대 길이 — 넘으면 말줄임
    private static readonly WpfBrush DotSuccess = new(WpfColor.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly WpfBrush DotNotice  = new(WpfColor.FromRgb(0xF0, 0xA8, 0x68));
    private static readonly WpfBrush DotReady   = new(WpfColor.FromRgb(0x81, 0x8C, 0xF8));

    private static readonly DoubleAnimation SpinAnimation = new(0, 360, TimeSpan.FromSeconds(0.85))
    {
        RepeatBehavior = RepeatBehavior.Forever
    };

    private DispatcherTimer? _hideTimer;
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc;

    public InputTranslationOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsApiHelper.EnableClickThrough(this);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _winEventProc, 0, 0, 0x0000); // EVENT_SYSTEM_FOREGROUND
        Closed += (_, _) => { if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook); };
    }

    // ── 공개 API ─────────────────────────────────────────────────────────
    // 핫키 핸들러(UI 스레드)에서 호출. 번역이 진행되는 동안 "번역 중"을 먼저 띄운다.
    public void ShowLoading(string message = "번역 중…")
    {
        Dispatcher.Invoke(() =>
        {
            _hideTimer?.Stop();
            SetState(spinner: true, dot: null);
            MessageText.Text = message;
            HintText.Visibility = Visibility.Collapsed;
            PositionOverlay();
        });
    }

    // 사용자가 텍스트를 복사한 직후 — 단축키만 누르면 번역된다는 안내.
    public void ShowReady()
    {
        Dispatcher.Invoke(() =>
        {
            SetState(spinner: false, dot: DotReady);
            MessageText.Text = "번역 준비 완료";
            HintText.Text = "번역을 원하면 단축키를 눌러주세요.";
            HintText.Visibility = Visibility.Visible;
            PositionOverlay();
            ScheduleAutoHide();
        });
    }

    // 결과 미리보기 표시용 — 클립보드엔 전체 번역문이 그대로 들어가고, 오버레이엔 길면 잘라서 보여준다.
    public void ShowResult(string translated)
    {
        Dispatcher.Invoke(() =>
        {
            SetState(spinner: false, dot: DotSuccess);
            MessageText.Text = Truncate(translated, MAX_RESULT_CHARS);
            HintText.Text = "Ctrl+V로 붙여넣으세요";
            HintText.Visibility = Visibility.Visible;
            PositionOverlay();
            ScheduleAutoHide();
        });
    }

    public void ShowNotice(string message)
    {
        Dispatcher.Invoke(() =>
        {
            SetState(spinner: false, dot: DotNotice);
            MessageText.Text = message;
            HintText.Visibility = Visibility.Collapsed;
            PositionOverlay();
            ScheduleAutoHide();
        });
    }

    // ── 내부 ─────────────────────────────────────────────────────────────
    private static string Truncate(string text, int maxChars) =>
        text.Length > maxChars ? text[..maxChars] + "…" : text;

    private void SetState(bool spinner, WpfBrush? dot)
    {
        if (spinner)
        {
            Spinner.Visibility = Visibility.Visible;
            StatusDot.Visibility = Visibility.Collapsed;
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, SpinAnimation);
        }
        else
        {
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            Spinner.Visibility = Visibility.Collapsed;
            StatusDot.Visibility = Visibility.Visible;
            StatusDot.Fill = dot ?? DotSuccess;
        }
    }

    private const int EDGE_MARGIN = 28;          // 폴백 시 좌측 가장자리 여백(물리 px)
    private const double VERTICAL_ANCHOR = 0.58;  // 폴백 시 세로 위치: 화면 중앙에서 살짝 아래
    private const int GAP_ABOVE_REGION = 10;      // 채팅 캡쳐 영역 상단과의 간격(물리 px)

    private System.Drawing.Rectangle _captureAnchor; // 채팅 캡쳐 영역(물리 px). 이 영역 바로 위에 오버레이를 띄운다.

    public void SetCaptureAnchor(System.Drawing.Rectangle region) => _captureAnchor = region;

    // 채팅 캡쳐 영역 상단보다 살짝 위, 좌측 정렬로 배치 — 채팅에 가 있는 시선 근처.
    // 영역 정보가 없으면(폴백) 마우스가 있는 모니터의 좌측, 세로 중앙보다 약간 아래에 둔다.
    private void PositionOverlay()
    {
        Show();
        UpdateLayout(); // SizeToContent 반영 후 ActualWidth/Height 확정
        var dpi = VisualTreeHelper.GetDpi(this);
        var h = ActualHeight * dpi.DpiScaleY;

        if (_captureAnchor.Width > 0)
        {
            Left = _captureAnchor.Left / dpi.DpiScaleX;
            Top  = (_captureAnchor.Top - GAP_ABOVE_REGION - h) / dpi.DpiScaleY;
            return;
        }

        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        var wa = screen.WorkingArea;
        Left = (wa.Left + EDGE_MARGIN) / dpi.DpiScaleX;
        Top  = (wa.Top + wa.Height * VERTICAL_ANCHOR - h / 2) / dpi.DpiScaleY;
    }

    private void ScheduleAutoHide()
    {
        // 단일 타이머를 재사용 — 호출마다 새로 만들지 않고 인터벌만 재시작한다.
        if (_hideTimer is null)
        {
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY_MS) };
            _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        }
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    // ── 클릭 통과 + 최상단 고정 (채팅 오버레이와 동일 패턴) ──────────────────
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eMin, uint eMax, IntPtr hmod, WinEventProc fn, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool   UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool   SetWindowPos(IntPtr hwnd, IntPtr hwndAfter, int x, int y, int cx, int cy, uint flags);

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint tid, uint time);
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint tid, uint time)
    {
        if (!IsVisible) return;
        var myHwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == myHwnd) return;
        SetWindowPos(myHwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0013); // SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) // WM_MOUSEACTIVATE — 클릭해도 활성화하지 않음
        {
            handled = true;
            return (IntPtr)3; // MA_NOACTIVATE
        }
        return IntPtr.Zero;
    }
}
