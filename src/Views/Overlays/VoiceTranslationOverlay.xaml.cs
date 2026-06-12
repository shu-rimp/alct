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

    // 최소 표시시간: 기본 + 글자당 가산, 상한은 AUTO_HIDE보다 짧게 (대기열이 auto-hide 전에 소화되도록)
    private const int DISPLAY_BASE_MS = 1000;
    private const int DISPLAY_PER_CHAR_MS = 60;
    private const int DISPLAY_MAX_MS = 4000;

    private static readonly WpfColor BgColor = WpfColor.FromRgb(0x16, 0x14, 0x1F);

    private DispatcherTimer? _hideTimer;
    private double _opacity = 0.7;
    private double _fontSize = 13;
    private bool _isEditMode;
    private bool _hasContent;
    private bool _isEditPlaceholder;
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc;

    // 자막 단위 = (원문, 번역) 쌍 — 화면의 원문과 번역이 항상 같은 발화를 가리키도록
    private sealed class SubtitleEntry
    {
        public string Original = "";
        public string? Translation;
    }

    // 메인 자막 패널 — 작은 원문 + 번역 두 줄, 영화 자막처럼 한 쌍만 표시
    private StackPanel? _mainPanel;
    private TextBlock? _originalTextBlock;
    private TextBlock? _translationTextBlock;

    // 최소 표시시간 보장 — 현재 쌍을 읽기 전에 다음 쌍이 덮어쓰지 않도록 대기열로 지연
    private SubtitleEntry? _currentEntry;
    private readonly Queue<SubtitleEntry> _entryQueue = new();
    private DateTime _displayUntil = DateTime.MinValue;
    private DispatcherTimer? _advanceTimer;

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
        if (_originalTextBlock != null)
            _originalTextBlock.FontSize = Math.Max(8, size - 2);
        if (_translationTextBlock != null)
            _translationTextBlock.FontSize = size;
        if (_pendingTextBlock != null)
            _pendingTextBlock.FontSize = Math.Max(8, size - 2);
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
        Width = 700;
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
                EnsureMainPanel();
                _originalTextBlock!.Visibility = Visibility.Collapsed;
                _translationTextBlock!.Text = "오늘 와주셔서 감사합니다.";
                _translationTextBlock.Visibility = Visibility.Visible;
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

            if (_isEditPlaceholder) ClearAllContent();

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

    // (1/2) 커밋 시점: 새 (원문, 번역 대기) 쌍을 대기열에 등록
    public void ShowOriginal(string original)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_isEditPlaceholder) ClearAllContent();
            _entryQueue.Enqueue(new SubtitleEntry { Original = original });
            TryAdvance();
        });
    }

    // (2/2) 번역 도착: 번역은 발사 순서대로 도착(직렬화)하므로
    // 번역 없는 첫 엔트리에 붙이면 원문-번역 쌍이 항상 일치
    public void ShowTranslation(string translated)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_isEditPlaceholder) ClearAllContent();

            if (_currentEntry != null && _currentEntry.Translation == null)
            {
                _currentEntry.Translation = translated;
                DisplayEntry(_currentEntry); // 표시 중인 원문에 번역 합류
                return;
            }

            var waiting = _entryQueue.FirstOrDefault(e => e.Translation == null);
            if (waiting != null)
            {
                waiting.Translation = translated;
                TryAdvance();
                return;
            }

            // 매칭할 원문 없음(auto-hide로 비워진 뒤 도착한 늦은 번역) — 번역 단독 표시
            _entryQueue.Enqueue(new SubtitleEntry { Translation = translated });
            TryAdvance();
        });
    }

    // 다음 쌍으로 넘어갈 수 있으면 넘어감
    // 조건: 현재 쌍이 없거나, 번역까지 표시된 채 최소 표시시간을 채웠을 때
    private void TryAdvance()
    {
        if (_entryQueue.Count == 0) return;

        if (_currentEntry != null)
        {
            // 번역이 아직 안 온 쌍은 건너뛰지 않음 — 다음 도착 번역이 이 쌍의 것
            if (_currentEntry.Translation == null) return;
            if (DateTime.UtcNow < _displayUntil)
            {
                ScheduleAdvance();
                return;
            }
        }

        var entry = _entryQueue.Dequeue();

        // 읽기 속도보다 빠르게 밀린 번역 완료 쌍들은 병합해서 한 번에 따라잡기
        while (entry.Translation != null &&
               _entryQueue.Count > 0 && _entryQueue.Peek().Translation != null)
        {
            var next = _entryQueue.Dequeue();
            entry.Original    = JoinNonEmpty(entry.Original, next.Original);
            entry.Translation = JoinNonEmpty(entry.Translation, next.Translation!);
        }

        DisplayEntry(entry);
    }

    private void DisplayEntry(SubtitleEntry entry)
    {
        _currentEntry = entry;
        EnsureMainPanel();

        _originalTextBlock!.Text       = entry.Original;
        _originalTextBlock.Visibility  = string.IsNullOrEmpty(entry.Original)
            ? Visibility.Collapsed : Visibility.Visible;

        _translationTextBlock!.Text      = entry.Translation ?? "";
        _translationTextBlock.Visibility = entry.Translation == null
            ? Visibility.Collapsed : Visibility.Visible;

        // 최소 표시시간은 번역이 표시된 시점부터 — 원문만 표시 중일 땐 미적용
        _displayUntil = entry.Translation != null
            ? DateTime.UtcNow + CalcDisplayTime(entry.Translation)
            : DateTime.MinValue;

        _hasContent        = true;
        _isEditPlaceholder = false;
        Show();
        ScheduleAutoHide();

        if (_entryQueue.Count > 0 && entry.Translation != null)
            ScheduleAdvance();
    }

    private static TimeSpan CalcDisplayTime(string text) =>
        TimeSpan.FromMilliseconds(Math.Min(DISPLAY_MAX_MS, DISPLAY_BASE_MS + text.Length * DISPLAY_PER_CHAR_MS));

    private void ScheduleAdvance()
    {
        if (_advanceTimer != null) return;

        var delay = _displayUntil - DateTime.UtcNow;
        if (delay < TimeSpan.FromMilliseconds(50))
            delay = TimeSpan.FromMilliseconds(50);

        _advanceTimer = new DispatcherTimer { Interval = delay };
        _advanceTimer.Tick += (_, _) =>
        {
            _advanceTimer!.Stop();
            _advanceTimer = null;
            TryAdvance();
        };
        _advanceTimer.Start();
    }

    private static string JoinNonEmpty(string a, string b) =>
        string.Join(" ", new[] { a, b }.Where(s => !string.IsNullOrEmpty(s)));

    // ── 패널 헬퍼 ─────────────────────────────────────────────────────────────

    // 메인 패널(작은 원문 + 번역)은 항상 최상단, pending은 그 아래
    private void EnsureMainPanel()
    {
        if (_mainPanel != null) return;

        _originalTextBlock = new TextBlock
        {
            Foreground   = (WpfBrushBase)FindResource("TextSecondaryBrush"),
            FontSize     = Math.Max(8, _fontSize - 2),
            FontStyle    = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        _translationTextBlock = new TextBlock
        {
            Foreground   = (WpfBrushBase)FindResource("TextPrimaryBrush"),
            FontSize     = _fontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 2, 0, 0),
        };
        _mainPanel = new StackPanel { Margin = new Thickness(0) };
        _mainPanel.Children.Add(_originalTextBlock);
        _mainPanel.Children.Add(_translationTextBlock);
        TranslationList.Children.Insert(0, _mainPanel);
        if (_pendingPanel != null)
            _pendingPanel.Margin = new Thickness(0, 8, 0, 0);
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
        _mainPanel            = null;
        _originalTextBlock    = null;
        _translationTextBlock = null;
        _pendingPanel         = null;
        _pendingTextBlock     = null;
        _hasContent           = false;
        _isEditPlaceholder    = false;
        _currentEntry         = null;
        _entryQueue.Clear();
        _advanceTimer?.Stop();
        _advanceTimer = null;
        _displayUntil = DateTime.MinValue;
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
