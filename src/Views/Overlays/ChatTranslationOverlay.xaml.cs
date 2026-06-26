using AlctClient.Utils;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AlctClient.Views.Overlays;

// 캡처 영역을 덮는 투명 오버레이. 각 OCR 줄의 번역문을 원문 위치 위에(반투명 검정 배경 + 흰 글씨)
// 배치한다. 부가 디자인 요소(테두리/헤더/구분선) 없이 텍스트만 표시.
public partial class ChatTranslationOverlay : Window
{
    private const int NOTICE_HIDE_MS = 5000;      // 안내/오류는 설정과 무관하게 짧게 자동 숨김
    private const double DEFAULT_FONT = 14.0;     // UserSettings.OverlayFontSize 기본값 — 스케일 1.0 기준
    private const double BOX_FONT_RATIO = 0.62;   // 박스 높이 → 글자 크기(대략적인 cap height 비율)
    private const double MIN_FONT = 9.0;
    private const double MAX_FONT = 48.0;

    private static readonly WpfBrush TextBrush = new(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
    // 텍스트만 부각되도록 반투명 검정(50%) 배경. 너무 진하면 원문/배경을 과하게 가린다.
    private static readonly WpfBrush LabelBg = new(WpfColor.FromRgb(0, 0, 0)) { Opacity = 0.5 };

    private static readonly DoubleAnimation SpinAnimation = new(0, 360, TimeSpan.FromSeconds(0.85))
    {
        RepeatBehavior = RepeatBehavior.Forever
    };

    private readonly EscHintOverlay _hint = new();  // 화면 좌측 하단 고정 ESC 안내(별도 창)
    private DispatcherTimer? _hideTimer;
    private Rectangle _lastRegion;  // 마지막으로 창을 맞춘 캡처 영역 — ShowNotice 위치 폴백용
    private double _fontScale = 1.0;
    private int _autoHideMs = 5000;  // 채팅 번역 표시 시간(ms). 0 = 무제한
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc;

    public ChatTranslationOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void SetFontSize(double size) => _fontScale = size / DEFAULT_FONT;

    // 0 이하 = 무제한(타이머 없음 — ESC 또는 다음 캡처로만 사라짐)
    public void SetAutoHideSeconds(int seconds) => _autoHideMs = seconds > 0 ? seconds * 1000 : 0;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsApiHelper.EnableClickThrough(this);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _winEventProc, 0, 0, 0x0000); // EVENT_SYSTEM_FOREGROUND
        Closed += (_, _) => { if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook); _hint.Close(); };
    }

    // ── 공개 API ─────────────────────────────────────────────────────────

    // 캡처 직후 "번역 중" 표시. 창을 캡처 영역에 맞추고 이전 결과는 비운다.
    public void ShowLoading(Rectangle captureRegion, string message = "번역 중…")
    {
        Dispatcher.Invoke(() =>
        {
            _hideTimer?.Stop();
            Show();
            PositionWindow(captureRegion);
            RegionCanvas.Children.Clear();
            _hint.Hide();
            SpinnerBox.Visibility = Visibility.Visible;
            StatusText.Text = message;
            StatusPill.Visibility = Visibility.Visible;
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, SpinAnimation);
        });
    }

    // 결과 없이 끝난 경우(번역 실패 등) 스피너만 정리.
    public void HideLoading() => Dispatcher.Invoke(() =>
    {
        StopSpinner();
        if (RegionCanvas.Children.Count == 0) Hide();
    });

    // ESC/다음 캡처로 즉시 숨김.
    public void HideNow() => Dispatcher.Invoke(() =>
    {
        _hideTimer?.Stop();
        RegionCanvas.Children.Clear();
        StatusPill.Visibility = Visibility.Collapsed;
        _hint.Hide();
        Hide();
    });

    // 안내/오류 메시지 — 좌상단 알약에 표시 후 자동 숨김. 창은 이미 ShowLoading이 배치해 둠.
    public void ShowNotice(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StopSpinner();
            RegionCanvas.Children.Clear();
            _hint.Hide();
            Show();
            if (_lastRegion.Width > 0) PositionWindow(_lastRegion);  // 직전 캡처 영역에 맞춤(없으면 현재 위치 유지)
            SpinnerBox.Visibility = Visibility.Collapsed;
            StatusText.Text = message;
            StatusPill.Visibility = Visibility.Visible;
            ScheduleHide(NOTICE_HIDE_MS);  // 안내는 설정과 무관하게 항상 자동 숨김
        });
    }

    // 번역 결과를 원문 위치 위에 배치. bounds는 캡처 영역 기준 픽셀(좌상단 0,0).
    public void ShowTranslations(IReadOnlyList<(string Text, RectangleF Bounds)> regions, Rectangle captureRegion)
    {
        Dispatcher.Invoke(() =>
        {
            StopSpinner();
            StatusPill.Visibility = Visibility.Collapsed;
            RegionCanvas.Children.Clear();

            Show();
            var dpi = PositionWindow(captureRegion);
            double maxRightDiu = captureRegion.Width / dpi.DpiScaleX;

            foreach (var (text, b) in regions)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                RegionCanvas.Children.Add(BuildLabel(text, b, dpi, maxRightDiu));
            }

            if (RegionCanvas.Children.Count == 0) { Hide(); return; }
            _hint.ShowAtScreen(captureRegion);  // "ESC를 눌러 번역 숨기기" — 화면 좌측 하단 고정
            ScheduleHide(_autoHideMs);
        });
    }

    // ── 내부 ─────────────────────────────────────────────────────────────

    // 창을 캡처 영역(물리 px)에 맞춘다 — DIU로 환산. 사용한 DpiScale을 돌려준다.
    private DpiScale PositionWindow(Rectangle region)
    {
        _lastRegion = region;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left   = region.Left   / dpi.DpiScaleX;
        Top    = region.Top    / dpi.DpiScaleY;
        Width  = region.Width  / dpi.DpiScaleX;
        Height = region.Height / dpi.DpiScaleY;
        return dpi;
    }

    private Border BuildLabel(string text, RectangleF b, DpiScale dpi, double maxRightDiu)
    {
        double left      = b.Left   / dpi.DpiScaleX;
        double top       = b.Top    / dpi.DpiScaleY;
        double widthDiu  = b.Width  / dpi.DpiScaleX;
        double heightDiu = b.Height / dpi.DpiScaleY;
        double fontSize  = Math.Clamp(heightDiu * BOX_FONT_RATIO, MIN_FONT, MAX_FONT) * _fontScale;

        var label = new Border
        {
            Background = LabelBg,
            Padding = new Thickness(2, 0, 2, 0),
            MaxWidth = Math.Max(widthDiu, maxRightDiu - left),   // 번역문 길이에 맞춤(여백 없음), 너무 길면 영역 안에서 줄바꿈
            Child = new TextBlock
            {
                Text = text,
                Foreground = TextBrush,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        return label;
    }

    private void StopSpinner()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        SpinnerBox.Visibility = Visibility.Collapsed;
    }

    private void ScheduleHide(int ms)
    {
        _hideTimer?.Stop();
        if (ms <= 0) return;  // 무제한 — ESC 또는 다음 캡처로만 숨김
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HideNow(); };
        _hideTimer.Start();
    }

    // ── 클릭 통과 + 최상단 고정 (다른 오버레이와 동일 패턴) ──────────────────
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
