using System.Windows;
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
    private bool _isEditMode;
    private bool _hasContent;
    private bool _isPlaceholder;

    public VoiceTranslationOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 30;
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
            if (!_hasContent)
            {
                TranslatedText.Text = "오늘 와주셔서 감사합니다.";
                OriginalText.Text   = "本日はお越しいただきありがとうございます。";
                _isPlaceholder = true;
            }
            Show();
        }
        else
        {
            if (_isPlaceholder)
            {
                TranslatedText.Text = string.Empty;
                OriginalText.Text   = string.Empty;
                _isPlaceholder = false;
            }
            if (!_hasContent) Hide();
        }
    }

    public void ShowTranslation(string translated, string original)
    {
        Dispatcher.Invoke(() =>
        {
            TranslatedText.Text = translated;
            OriginalText.Text   = original;
            _hasContent    = true;
            _isPlaceholder = false;
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
            _hasContent = false;
            if (!_isEditMode) Hide();
        };
        _hideTimer.Start();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditMode) DragMove();
    }
}
