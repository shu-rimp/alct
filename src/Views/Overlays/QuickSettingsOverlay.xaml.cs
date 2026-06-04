using AlctClient.Utils;
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
        WindowsApiHelper.ExcludeFromCapture(this);
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(NoActivateHook);
    }

    private static IntPtr NoActivateHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) // WM_MOUSEACTIVATE
        {
            handled = true;
            return (IntPtr)3; // MA_NOACTIVATE
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
