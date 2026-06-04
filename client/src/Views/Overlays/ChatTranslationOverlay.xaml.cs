using AlctClient.Utils;
using System.Collections.ObjectModel;
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

    private static readonly TranslationEntry[] Placeholder =
    [
        new("좋은 아침이에요! 오늘도 잘 부탁해요.", "おはようございます！今日もよろしくお願いします。"),
        new("잠깐만 기다려 주세요.",               "ちょっと待ってください。"),
    ];

    public ChatTranslationOverlay()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _entries;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsApiHelper.ExcludeFromCapture(this);
        if (!_isEditMode) WindowsApiHelper.EnableClickThrough(this);
        ApplyOpacity();
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) // WM_MOUSEACTIVATE
        {
            handled = true;
            return (IntPtr)3; // MA_NOACTIVATE
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
        Left = SystemParameters.PrimaryScreenWidth - Width - 20;
        Top  = 80;
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
