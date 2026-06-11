using AlctClient.Utils;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AlctClient.Views.Overlays;

public record TranslationEntry(string Translated, string Original);

public partial class ChatTranslationOverlay : Window
{
    private const int MAX_ENTRIES = 5;
    private const int AUTO_HIDE_DELAY_MS = 5000;
    private static readonly WpfColor BgColor = WpfColor.FromRgb(0x16, 0x14, 0x1F);

    private readonly ObservableCollection<TranslationEntry> _entries = new();
    private DispatcherTimer? _hideTimer;
    private double _opacity = 0.7;
    private bool _isEditMode;
    private bool _isPlaceholder;
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc;

    private static readonly TranslationEntry[] Placeholder =
    [
        new("좋은 아침이에요! 오늘도 잘 부탁해요.", "おはようございます！今日もよろしくお願いします。"),
        new("잠깐만 기다려 주세요.",               "ちょっと待ってください。"),
    ];

    public ChatTranslationOverlay()
    {
        InitializeComponent();
        Resources["OverlayFontSizeMain"] = 13.0;
        Resources["OverlayFontSizeSub"]  = 11.0;
        EntriesList.ItemsSource = _entries;
        Loaded += OnLoaded;
    }

    public void SetFontSize(double size)
    {
        Resources["OverlayFontSizeMain"] = size;
        Resources["OverlayFontSizeSub"]  = Math.Max(8.0, size - 2);
    }

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
            EntriesList.InvalidateMeasure();
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
                EntriesList.InvalidateMeasure();
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

    private void SnapToDefaultPosition()
    {
        Left = 20;
        Top  = (SystemParameters.PrimaryScreenHeight - 120) / 2;
    }

    public void MoveToMonitor(System.Windows.Forms.Screen screen)
    {
        Left = screen.Bounds.Left + 20;
        Top  = screen.Bounds.Top  + (screen.Bounds.Height - 120) / 2;
    }

    public void ResetBounds(System.Windows.Forms.Screen screen)
    {
        Width = 280;
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

    public void SetEditMode(bool enabled)
    {
        _isEditMode = enabled;
        EditModeBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Cursor = enabled ? WpfCursors.SizeAll : WpfCursors.Arrow;

        if (enabled)
        {
            if (_entries.Count == 0)
            {
                foreach (var e in Placeholder) _entries.Add(e);
                _isPlaceholder = true;
            }
            Show();
            WindowsApiHelper.DisableClickThrough(this);
        }
        else
        {
            if (_isPlaceholder)
            {
                _entries.Clear();
                _isPlaceholder = false;
            }
            WindowsApiHelper.EnableClickThrough(this);
            if (_entries.Count == 0) Hide();
        }
    }

    public void ShowNotice(string message) => ShowTranslation(message, string.Empty);

    public void ShowTranslation(string translated, string original)
    {
        Dispatcher.Invoke(() =>
        {
            _entries.Clear();
            _isPlaceholder = false;

            var translatedLines = translated.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var originalLines   = original.Split('\n',   StringSplitOptions.RemoveEmptyEntries);
            var count = Math.Max(translatedLines.Length, originalLines.Length);

            for (int i = 0; i < count && i < MAX_ENTRIES; i++)
            {
                var t = i < translatedLines.Length ? translatedLines[i].Trim() : string.Empty;
                var o = i < originalLines.Length   ? originalLines[i].Trim()   : string.Empty;
                if (!string.IsNullOrWhiteSpace(t) || !string.IsNullOrWhiteSpace(o))
                    _entries.Add(new TranslationEntry(t, o));
            }

            Show();
            ScheduleAutoHide();
        });
    }

    public void ShowAtLiveCaptions(string translated, string original)
    {
        Dispatcher.Invoke(() =>
        {
            _entries.Clear();
            _isPlaceholder = false;
            _entries.Add(new TranslationEntry(translated, original));
            Show();
            ScheduleAutoHide();
        });
    }

    private void ScheduleAutoHide()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY_MS) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_isPlaceholder) _entries.Clear();
            if (!_isEditMode) Hide();
        };
        _hideTimer.Start();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditMode) DragMove();
    }
}
