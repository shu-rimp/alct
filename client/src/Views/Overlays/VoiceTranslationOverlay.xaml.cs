using AlctClient.Utils;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AlctClient.Views.Overlays;

public partial class VoiceTranslationOverlay : Window
{
    private const int AUTO_HIDE_DELAY_MS = 5000;
    private static readonly WpfColor BgColor = WpfColor.FromRgb(0x16, 0x14, 0x1F);

    private DispatcherTimer? _hideTimer;
    private double _opacity = 0.7;
    private double _fontSize = 13;
    private bool _isEditMode;
    private bool _hasContent;
    private bool _isEditPlaceholder; // SetEditMode에서만 true — 편집모드 미리보기 텍스트
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc;

    // 번역 결과 패널 (항상 1개 — 재활용)
    private StackPanel? _contentPanel;

    // 실시간 발화 pending 패널
    private StackPanel? _pendingPanel;
    private TextBlock? _pendingTextBlock;

    public VoiceTranslationOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── 초기화 ────────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isEditMode) WindowsApiHelper.EnableClickThrough(this);
        ApplyOpacity();
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _winEventProc, 0, 0, 0x0000); // EVENT_SYSTEM_FOREGROUND
        Closed += (_, _) => { if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook); };
        SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged) return;
            Width = ActualWidth;
            TranslationList.InvalidateMeasure();
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.Height;
        };
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

    private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint tid, uint time)
    {
        if (!IsVisible) return;
        var myHwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == myHwnd) return;
        SetWindowPos(myHwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0013); // SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

        if (msg == 0x0232) // WM_EXITSIZEMOVE — 드래그 완료 후 최종 높이 보정
        {
            Dispatcher.InvokeAsync(() =>
            {
                Width = ActualWidth;
                TranslationList.InvalidateMeasure();
                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
            });
        }

        if (_isEditMode && msg == 0x0084) // WM_NCHITTEST
        {
            var pos = PointFromScreen(new WpfPoint(
                (short)(lParam.ToInt32() & 0xFFFF),
                (short)((lParam.ToInt32() >> 16) & 0xFFFF)));

            const int B = 8;
            if (pos.X < B)               { handled = true; return (IntPtr)10; } // HTLEFT
            if (pos.X > ActualWidth - B) { handled = true; return (IntPtr)11; } // HTRIGHT
        }

        return IntPtr.Zero;
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.1, 1.0);
        if (IsLoaded) ApplyOpacity();
    }

    private void ApplyOpacity() =>
        RootBorder.Background = new WpfBrush(BgColor) { Opacity = _opacity };

    public void SetFontSize(double size)
    {
        _fontSize = size;
        foreach (var sp in TranslationList.Children.OfType<StackPanel>())
            foreach (var tb in sp.Children.OfType<TextBlock>())
                tb.FontSize = tb.Tag as string == "main" ? size : Math.Max(8, size - 2);
    }

    // ── 위치 ──────────────────────────────────────────────────────────────────

    private void SnapToDefaultPosition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 30;
    }

    public void MoveToMonitor(System.Windows.Forms.Screen screen)
    {
        Left = screen.Bounds.Left + (screen.Bounds.Width - Width) / 2;
        Top  = screen.Bounds.Top  + 30;
    }

    public void ResetBounds(System.Windows.Forms.Screen screen)
    {
        Width = 500;
        MoveToMonitor(screen);
    }

    public void LoadBounds(double left, double top, double width)
    {
        Width = width;
        if (left < 0)
            SnapToDefaultPosition();
        else
        {
            Left = left;
            Top  = top;
        }
    }

    // ── 편집 모드 ─────────────────────────────────────────────────────────────

    public void SetEditMode(bool enabled)
    {
        _isEditMode = enabled;
        EditModeBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Cursor = enabled ? WpfCursors.SizeAll : WpfCursors.Arrow;

        if (enabled)
        {
            if (!_hasContent)
            {
                EnsureContentPanel();
                var primaryBrush = (WpfBrushBase)FindResource("TextPrimaryBrush");
                _contentPanel!.Children.Clear();
                _contentPanel.Children.Add(new TextBlock
                {
                    Text         = "오늘 와주셔서 감사합니다.",
                    Foreground   = primaryBrush,
                    FontSize     = _fontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Tag          = "main",
                });
                _hasContent        = true;
                _isEditPlaceholder = true;
            }
            Show();
            WindowsApiHelper.DisableClickThrough(this);
        }
        else
        {
            if (_isEditPlaceholder) ClearAllContent();
            WindowsApiHelper.EnableClickThrough(this);
            if (!_hasContent) Hide();
        }
    }

    // ── 내용 표시 ─────────────────────────────────────────────────────────────

    // 실시간 발화 표시 (항상 최하단). delta가 비어있으면 제거
    public void ShowPending(string delta)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(delta))
            {
                RemovePendingPanel();
                return;
            }

            if (_pendingPanel == null)
            {
                var secondaryBrush = (WpfBrushBase)FindResource("TextSecondaryBrush");
                _pendingTextBlock = new TextBlock
                {
                    Text         = delta,
                    Foreground   = secondaryBrush,
                    FontSize     = Math.Max(8, _fontSize - 2),
                    FontStyle    = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Tag          = "sub",
                };
                _pendingPanel = new StackPanel
                {
                    Margin = TranslationList.Children.Count > 0
                        ? new Thickness(0, 8, 0, 0)
                        : new Thickness(0),
                };
                _pendingPanel.Children.Add(_pendingTextBlock);
                TranslationList.Children.Add(_pendingPanel);
            }
            else
            {
                _pendingTextBlock!.Text = delta;
            }

            _hasContent        = true;
            _isEditPlaceholder = false;
            Show();
            ScheduleAutoHide();
        });
    }

    // 번역 흐름: ShowOriginalPinned → (번역 완료) → ShowTranslation
    // 두 메서드는 동일한 _contentPanel을 재활용한다.

    // (1/2) 번역 대기: pending 제거 후 원문을 이탤릭으로 표시
    public void ShowOriginalPinned(string original)
    {
        Dispatcher.Invoke(() =>
        {
            // 같은 텍스트가 동시에 두 줄 보이는 것 방지
            RemovePendingPanel();
            EnsureContentPanel();

            var secondaryBrush = (WpfBrushBase)FindResource("TextSecondaryBrush");
            _contentPanel!.Children.Clear();
            _contentPanel.Children.Add(new TextBlock
            {
                Text         = original,
                Foreground   = secondaryBrush,
                FontSize     = Math.Max(8, _fontSize - 2),
                FontStyle    = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Tag          = "sub",
            });

            _hasContent        = true;
            _isEditPlaceholder = false;
            Show();
            ScheduleAutoHide();
        });
    }

    // (2/2) 번역 완료: 같은 패널에 번역 결과로 교체
    public void ShowTranslation(string translated)
    {
        Dispatcher.Invoke(() =>
        {
            EnsureContentPanel();

            var primaryBrush = (WpfBrushBase)FindResource("TextPrimaryBrush");
            _contentPanel!.Children.Clear();
            _contentPanel.Children.Add(new TextBlock
            {
                Text         = translated,
                Foreground   = primaryBrush,
                FontSize     = _fontSize,
                TextWrapping = TextWrapping.Wrap,
                Tag          = "main",
            });

            _hasContent        = true;
            _isEditPlaceholder = false;
            Show();
            ScheduleAutoHide();
        });
    }

    // ── 패널 헬퍼 ─────────────────────────────────────────────────────────────

    // _contentPanel이 없으면 생성해서 pending 앞에 삽입
    // 순서 보장: [content: 원문/번역] [pending: 실시간 발화중...]
    private void EnsureContentPanel()
    {
        if (_contentPanel != null) return;

        _contentPanel = new StackPanel { Margin = new Thickness(0) };

        if (_pendingPanel != null)
        {
            var idx = TranslationList.Children.IndexOf(_pendingPanel);
            TranslationList.Children.Insert(idx, _contentPanel);
            _pendingPanel.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            TranslationList.Children.Add(_contentPanel);
        }
    }

    private void RemovePendingPanel()
    {
        if (_pendingPanel == null) return;
        TranslationList.Children.Remove(_pendingPanel);
        _pendingPanel     = null;
        _pendingTextBlock = null;
    }

    private void ClearAllContent()
    {
        TranslationList.Children.Clear();
        _contentPanel      = null;
        _pendingPanel      = null;
        _pendingTextBlock  = null;
        _hasContent        = false;
        _isEditPlaceholder = false;
    }

    // ── 자동 숨김 ─────────────────────────────────────────────────────────────

    private void ScheduleAutoHide()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY_MS) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            ClearAllContent();
            if (!_isEditMode) Hide();
        };
        _hideTimer.Start();
    }

    // ── 입력 ──────────────────────────────────────────────────────────────────

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditMode) DragMove();
    }
}
