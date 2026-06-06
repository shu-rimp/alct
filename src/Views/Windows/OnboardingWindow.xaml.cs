using AlctClient.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Clipboard = System.Windows.Clipboard;

namespace AlctClient.Views.Windows;

public partial class OnboardingWindow : Window
{
    private const int STEP_WELCOME = 0;
    private const int STEP_APIKEY  = 1;
    private const int STEP_LANG    = 2;
    private const int STEP_OVERLAY = 3;
    private const int STEP_DONE    = 4;

    private static readonly int[] DotMap = { 0, 1, 2, 3, 4 };

    private FrameworkElement[]? _panels;
    private Ellipse[]? _dots;

    private readonly Dictionary<string, bool> _installStatus = new()
    {
        ["ja-JP"] = false,
        ["zh-CN"] = false,
    };

    private readonly uint _captureMods;
    private readonly uint _captureVKey;
    private readonly uint _inputMods;
    private readonly uint _inputVKey;

    public event Action? OverlayCheckEntered;
    public event Action? OverlayCheckExited;
    public event Action<string>? ApiKeyConfirmed;

    public OnboardingWindow(uint captureMods, uint captureVKey, uint inputMods, uint inputVKey)
    {
        InitializeComponent();
        _captureMods = captureMods;
        _captureVKey = captureVKey;
        _inputMods   = inputMods;
        _inputVKey   = inputVKey;
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _panels = new FrameworkElement[] { Step0Panel, Step1Panel, Step2aPanel, Step2cPanel, Step3Panel };
        _dots   = new Ellipse[] { Dot0, Dot1, Dot2, Dot3, Dot4 };
        CaptureHotkeyLabel.Text = HotkeyManager.FormatHotkey(_captureMods, _captureVKey);
        InputHotkeyLabel.Text   = HotkeyManager.FormatHotkey(_inputMods, _inputVKey);
    }

    private void GoToStep(int step)
    {
        if (_panels == null) return;

        // Stop GIF when leaving language step
        if (step != STEP_LANG) StopVideoPlayback();

        foreach (var p in _panels) p.Visibility = Visibility.Collapsed;
        _panels[step].Visibility = Visibility.Visible;
        UpdateDots(step);

        if (step == STEP_LANG)
        {
            ResetLangStep();
            _ = CheckInstallStatusAsync();
            StartVideoPlayback();
            LaunchLiveCaptions();
        }
        else if (step == STEP_OVERLAY)
        {
            Topmost = true;
            OverlayCheckEntered?.Invoke();
        }
    }

    private void UpdateDots(int step)
    {
        if (_dots == null) return;
        var active   = (SolidColorBrush)FindResource("AccentBrush");
        var inactive = (SolidColorBrush)FindResource("TextMutedBrush");
        int activeDot = DotMap[step];
        for (int i = 0; i < _dots.Length; i++)
            _dots[i].Fill = i == activeDot ? active : inactive;
    }

    // ── Step 0 ──

    private void OnStep0Start(object sender, RoutedEventArgs e) => GoToStep(STEP_APIKEY);

    // ── Step 1: DeepL Key ──

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ApiKeyStatus.Text       = "DeepL API 키를 입력하거나 나중에 설정하세요.";
            ApiKeyStatus.Foreground = (SolidColorBrush)FindResource("TextMutedBrush");
        }
        else
        {
            ApiKeyStatus.Text       = "✓ 키가 입력되었습니다.";
            ApiKeyStatus.Foreground = (SolidColorBrush)FindResource("TextSuccessBrush");
        }
    }

    private void OnPasteApiKey(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            ApiKeyBox.Password = Clipboard.GetText().Trim();
            OnApiKeyChanged(sender, e);
        }
    }

    private void OnSkipApiKey(object sender, RoutedEventArgs e) => GoToStep(STEP_LANG);

    private void OnStep1Next(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (!string.IsNullOrEmpty(key))
            ApiKeyConfirmed?.Invoke(key);
        GoToStep(STEP_LANG);
    }

    private void OnBack1(object sender, RoutedEventArgs e) => GoToStep(STEP_WELCOME);

    // ── Step 2a: Language Pack ──

    private async Task CheckInstallStatusAsync()
    {
        JpStatus.Text = ZhStatus.Text = "확인 중...";
        JpStatus.Foreground = ZhStatus.Foreground =
            (SolidColorBrush)FindResource("TextMutedBrush");

        await Task.WhenAll(
            CheckOneAsync("ja-JP", JpStatus),
            CheckOneAsync("zh-CN", ZhStatus));
    }

    private async Task CheckOneAsync(string lang, TextBlock statusBlock)
    {
        var installed = await LanguagePackService.IsInstalledAsync(lang);
        _installStatus[lang] = installed;
        Dispatcher.Invoke(() =>
        {
            statusBlock.Text = installed ? "✓ 설치됨" : "설치 필요";
            statusBlock.Foreground = installed
                ? (SolidColorBrush)FindResource("TextSuccessBrush")
                : (SolidColorBrush)FindResource("TextMutedBrush");
        });
    }

    private async void OnCheckInstall(object sender, RoutedEventArgs e)
    {
        if (LangCheckBtn.Content as string == "다음") { GoToStep(STEP_OVERLAY); return; }

        LangCheckBtn.IsEnabled = false;
        await CheckInstallStatusAsync();
        LangCheckBtn.IsEnabled = true;

        if (_installStatus["ja-JP"] && _installStatus["zh-CN"])
            LangCheckBtn.Content = "다음";
    }

    private void OnSkipLanguage(object sender, RoutedEventArgs e) => GoToStep(STEP_OVERLAY);

    private void OnBack2a(object sender, RoutedEventArgs e) => GoToStep(STEP_APIKEY);

    private void ResetLangStep()
    {
        LangCheckBtn.Content   = "설치 확인";
        LangCheckBtn.IsEnabled = true;
        JpStatus.Text = ZhStatus.Text = "확인 중...";
        JpStatus.Foreground = ZhStatus.Foreground =
            (SolidColorBrush)FindResource("TextMutedBrush");
    }

    // ── Video playback ──

    private void StartVideoPlayback()
    {
        var path = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                  "assets", "livecaptions-install.mp4");
        if (!IOFile.Exists(path)) return;
        InstallGuideMedia.Source = new Uri(path, UriKind.Absolute);
        InstallGuideMedia.Play();
    }

    private void OnGuideMediaEnded(object sender, RoutedEventArgs e)
    {
        InstallGuideMedia.Position = TimeSpan.Zero;
        InstallGuideMedia.Play();
    }

    private void OnOpenLiveCaptions(object sender, RoutedEventArgs e) => LaunchLiveCaptions();

    private static void LaunchLiveCaptions()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(@"C:\Windows\System32\LiveCaptions.exe")
                { UseShellExecute = true });
        }
        catch { }
    }

    private void StopVideoPlayback()
    {
        InstallGuideMedia.Stop();
        InstallGuideMedia.Source = null;
    }

    // ── Step 2c: Overlay Check ──

    private void OnBack2c(object sender, RoutedEventArgs e)
    {
        OverlayCheckExited?.Invoke();
        GoToStep(STEP_LANG);
    }

    private void OnOverlayCheckDone(object sender, RoutedEventArgs e)
    {
        OverlayCheckExited?.Invoke();
        GoToStep(STEP_DONE);
    }

    // ── Step 3: Complete ──

    private void OnComplete(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCloseApp(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult != true)
            Application.Current.Shutdown();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopVideoPlayback();
        base.OnClosed(e);
    }
}
