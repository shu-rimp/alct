using AlctClient.Core;
using AlctClient.Views.Modals;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace AlctClient.Views.Windows;

public partial class SettingsWindow : Window
{
    public event Action<string>? SourceLangChanged;
    public event Action<bool>? CaptionModeChanged;
    public event Action<string>? DeepLApiKeyChanged;
    public event Action<string>? GeminiApiKeyChanged;
    public event Action<TranslationEngine>? VoiceEngineChanged;
    public event Action<TranslationEngine>? TextEngineChanged;
    public event Action<int>? MonitorIndexChanged;
    public event Action<bool>? ShowLanguageOverlayChanged;
    public event Action? OverlayPositionEditRequested;
    public event Action? ChangeCaptureHotkeyRequested;
    public event Action? ChangeInputHotkeyRequested;
    public event Action? SetCaptureRegionRequested;
    public event Action<bool>? CaptureRegionModeChanged; // true = 직접 지정

    private bool _allowClose;
    private bool _suppressMonitorEvent;
    private string _deepLApiKey  = string.Empty;
    private string _geminiApiKey = string.Empty;

    public string SourceLang
    {
        get
        {
            var selected = SourceLangCombo.SelectedItem as ComboBoxItem;
            return selected?.Tag as string ?? "ja-JP";
        }
    }

    public string DeepLApiKey => _deepLApiKey;

    public TranslationEngine SelectedVoiceEngine =>
        ((VoiceEngineCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "DeepL"  => TranslationEngine.DeepL,
            "Gemini" => TranslationEngine.Gemini,
            _        => TranslationEngine.MyMemory,
        };

    public TranslationEngine SelectedTextEngine =>
        ((TextEngineCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "DeepL"  => TranslationEngine.DeepL,
            "Gemini" => TranslationEngine.Gemini,
            _        => TranslationEngine.MyMemory,
        };

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetWindowIcon();
        LoadMonitors();
    }

    private void SetWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "alct.ico");
        if (!File.Exists(path)) return;
        using var icon = new System.Drawing.Icon(path, 32, 32);
        Icon = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
    }

    private void LoadMonitors()
    {
        MonitorCombo.Items.Clear();
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var label = $"모니터{i + 1}: ({s.Bounds.Width}×{s.Bounds.Height})" +
                        (s.Primary ? " (기본)" : "");
            MonitorCombo.Items.Add(new ComboBoxItem { Content = label });
        }
        if (MonitorCombo.Items.Count > 0)
            MonitorCombo.SelectedIndex = 0;
    }

    // ── 외부에서 초기값 설정 ──

    public void SetDeepLApiKey(string key)
    {
        _deepLApiKey = key;
        UpdateApiKeyWarnings();
    }

    public void SetGeminiApiKey(string key)
    {
        _geminiApiKey = key;
        UpdateApiKeyWarnings();
    }

    public void SetSourceLang(string lang)
    {
        foreach (ComboBoxItem item in SourceLangCombo.Items)
        {
            if (item.Tag as string == lang)
            {
                SourceLangCombo.SelectedItem = item;
                return;
            }
        }
    }

    public void SetCaptionMode(bool enabled) => CaptionMonitorToggle.IsChecked = enabled;

    public void SetShowLanguageOverlay(bool show) => ShowLangOverlayToggle.IsChecked = show;

    public void SetMonitorIndex(int index)
    {
        _suppressMonitorEvent = true;
        if (index >= 0 && index < MonitorCombo.Items.Count)
            MonitorCombo.SelectedIndex = index;
        _suppressMonitorEvent = false;
    }

    public void SetCaptureRegionMode(bool isCustom)
    {
        if (isCustom) RadioCaptureCustom.IsChecked = true;
        else RadioCaptureAuto.IsChecked = true;
    }

    public void SetCaptureHotkeyLabel(string text) => CaptureHotkeyLabel.Text = text;
    public void SetInputHotkeyLabel(string text)   => InputHotkeyLabel.Text   = text;

    public void SetVoiceEngine(TranslationEngine engine)
    {
        VoiceEngineCombo.SelectedIndex = engine switch
        {
            TranslationEngine.DeepL  => 1,
            TranslationEngine.Gemini => 2,
            _                        => 0,
        };
    }

    public void SetTextEngine(TranslationEngine engine)
    {
        TextEngineCombo.SelectedIndex = engine switch
        {
            TranslationEngine.DeepL  => 1,
            TranslationEngine.Gemini => 2,
            _                        => 0,
        };
    }

    internal void AllowClose() => _allowClose = true;

    // ── 탭 전환 ──

    private void SetActiveTab(int index)
    {
        TabTranslationBtn.Style = (Style)Resources["TabButtonInactive"];
        TabHotkeyBtn.Style     = (Style)Resources["TabButtonInactive"];
        TabScreenBtn.Style     = (Style)Resources["TabButtonInactive"];
        PanelTranslation.Visibility = Visibility.Collapsed;
        PanelHotkey.Visibility      = Visibility.Collapsed;
        PanelScreen.Visibility      = Visibility.Collapsed;

        switch (index)
        {
            case 0:
                TabTranslationBtn.Style = (Style)Resources["TabButtonActive"];
                PanelTranslation.Visibility = Visibility.Visible;
                break;
            case 1:
                TabHotkeyBtn.Style = (Style)Resources["TabButtonActive"];
                PanelHotkey.Visibility = Visibility.Visible;
                break;
            case 2:
                TabScreenBtn.Style = (Style)Resources["TabButtonActive"];
                PanelScreen.Visibility = Visibility.Visible;
                break;
        }

    }

    private void OnTabTranslation(object sender, RoutedEventArgs e) => SetActiveTab(0);
    private void OnTabHotkey(object sender, RoutedEventArgs e)      => SetActiveTab(1);
    private void OnTabScreen(object sender, RoutedEventArgs e)      => SetActiveTab(2);

    // ── 이벤트 핸들러 ──

    private void OnSourceLangChanged(object sender, SelectionChangedEventArgs e)
        => SourceLangChanged?.Invoke(SourceLang);

    private void OnVoiceEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        VoiceEngineChanged?.Invoke(SelectedVoiceEngine);
        UpdateApiKeyWarnings();
    }

    private void OnTextEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        TextEngineChanged?.Invoke(SelectedTextEngine);
        UpdateApiKeyWarnings();
    }

    private void OnCaptionModeChanged(object sender, RoutedEventArgs e)
        => CaptionModeChanged?.Invoke(CaptionMonitorToggle.IsChecked == true);

    private void OnApiSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ApiConfigModal(_deepLApiKey, _geminiApiKey) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (_deepLApiKey != dialog.DeepLApiKey)
            {
                _deepLApiKey = dialog.DeepLApiKey;
                DeepLApiKeyChanged?.Invoke(_deepLApiKey);
            }
            if (_geminiApiKey != dialog.GeminiApiKey)
            {
                _geminiApiKey = dialog.GeminiApiKey;
                GeminiApiKeyChanged?.Invoke(_geminiApiKey);
            }
            UpdateApiKeyWarnings();
        }
    }

    private void UpdateApiKeyWarnings()
    {
        if (TextApiKeyWarning is null || VoiceApiKeyWarning is null) return;

        TextApiKeyWarning.Visibility = SelectedTextEngine switch
        {
            TranslationEngine.DeepL   when string.IsNullOrEmpty(_deepLApiKey)  => Visibility.Visible,
            TranslationEngine.Gemini  when string.IsNullOrEmpty(_geminiApiKey) => Visibility.Visible,
            _ => Visibility.Collapsed,
        };

        VoiceApiKeyWarning.Visibility = SelectedVoiceEngine switch
        {
            TranslationEngine.DeepL   when string.IsNullOrEmpty(_deepLApiKey)  => Visibility.Visible,
            TranslationEngine.Gemini  when string.IsNullOrEmpty(_geminiApiKey) => Visibility.Visible,
            _ => Visibility.Collapsed,
        };
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonitorEvent) return;
        MonitorIndexChanged?.Invoke(MonitorCombo.SelectedIndex);
    }

    private void OnShowLangOverlayChanged(object sender, RoutedEventArgs e)
        => ShowLanguageOverlayChanged?.Invoke(ShowLangOverlayToggle.IsChecked == true);

    private void OnOverlayEditRequested(object sender, RoutedEventArgs e)
        => OverlayPositionEditRequested?.Invoke();

    private void OnChangeCaptureHotkey(object sender, RoutedEventArgs e)
        => ChangeCaptureHotkeyRequested?.Invoke();

    private void OnChangeInputHotkey(object sender, RoutedEventArgs e)
        => ChangeInputHotkeyRequested?.Invoke();

    private void OnSetCaptureRegion(object sender, RoutedEventArgs e)
        => SetCaptureRegionRequested?.Invoke();

    private void OnCaptureRegionModeChanged(object sender, RoutedEventArgs e)
        => CaptureRegionModeChanged?.Invoke(RadioCaptureCustom.IsChecked == true);

    // 커스텀 X 버튼: 트레이로 숨김
    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    // Alt+F4 등 OS 레벨 닫기: 앱 종료 중이 아니면 트레이로 숨김
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeHook);
    }

    private IntPtr ResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0084) return IntPtr.Zero; // WM_NCHITTEST

        var pos = PointFromScreen(new System.Windows.Point(
            (short)(lParam.ToInt32() & 0xFFFF),
            (short)((lParam.ToInt32() >> 16) & 0xFFFF)));

        const int B = 8;
        double w = ActualWidth, h = ActualHeight;
        bool left = pos.X < B,  right = pos.X > w - B;
        bool top  = pos.Y < B,  bottom = pos.Y > h - B;

        if (top    && left)  { handled = true; return (IntPtr)13; } // HTTOPLEFT
        if (top    && right) { handled = true; return (IntPtr)14; } // HTTOPRIGHT
        if (bottom && left)  { handled = true; return (IntPtr)16; } // HTBOTTOMLEFT
        if (bottom && right) { handled = true; return (IntPtr)17; } // HTBOTTOMRIGHT
        if (left)            { handled = true; return (IntPtr)10; } // HTLEFT
        if (right)           { handled = true; return (IntPtr)11; } // HTRIGHT
        if (top)             { handled = true; return (IntPtr)12; } // HTTOP
        if (bottom)          { handled = true; return (IntPtr)15; } // HTBOTTOM

        return IntPtr.Zero;
    }

    private void OnHeaderDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
}
