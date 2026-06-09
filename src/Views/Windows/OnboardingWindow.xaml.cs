using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Modals;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AlctClient.Views.Windows;

public partial class OnboardingWindow : Window
{
    private const int STEP_WELCOME     = 0;
    private const int STEP_CHAT        = 1;
    private const int STEP_INPUT       = 2;
    private const int STEP_VOICE       = 3;
    private const int STEP_VOICE_SETUP = 4;
    private const int STEP_DONE        = 5;

    // dot index per step (VOICE_SETUP shares dot with VOICE)
    private static readonly int[] DotMap = { 0, 1, 2, 3, 3, 4 };

    private FrameworkElement[]? _panels;
    private Ellipse[]? _dots;

    private readonly bool _voiceSupported;
    private readonly bool _installOnly;
    private readonly uint _captureMods;
    private readonly uint _captureVKey;
    private readonly uint _inputMods;
    private readonly uint _inputVKey;

    private readonly Dictionary<string, bool> _installStatus = new()
    {
        ["ja-JP"] = false,
        ["zh-CN"] = false,
    };

    private DispatcherTimer? _pollTimer;

    public event Action<string, string>? ApiKeysRegistered;
    public event Action? OnboardingCompleted;

    public OnboardingWindow(uint captureMods, uint captureVKey, uint inputMods, uint inputVKey)
    {
        _voiceSupported = WindowsApiHelper.IsLiveCaptionSupported();
        _installOnly    = false;
        InitializeComponent();
        _captureMods = captureMods;
        _captureVKey = captureVKey;
        _inputMods   = inputMods;
        _inputVKey   = inputVKey;
        Loaded += OnWindowLoaded;
    }

    private OnboardingWindow()
    {
        _voiceSupported = true;
        _installOnly    = true;
        InitializeComponent();
        Title = "음성 번역 준비";
        Loaded += OnWindowLoaded;
    }

    public static OnboardingWindow ForLanguagePackInstall() => new();

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _panels = new FrameworkElement[]
        {
            Step0Panel, StepChatPanel, StepInputPanel,
            StepVoicePanel, StepVoiceSetupPanel, StepDonePanel
        };
        _dots = new Ellipse[] { Dot0, Dot1, Dot2, Dot3, Dot4 };

        if (_installOnly)
        {
            DotsPanel.Visibility = Visibility.Collapsed;
            GoToStep(STEP_VOICE_SETUP);
            return;
        }

        ChatHotkeyLabel.Text  = HotkeyManager.FormatHotkey(_captureMods, _captureVKey);
        InputHotkeyLabel.Text = HotkeyManager.FormatHotkey(_inputMods, _inputVKey);

        if (!_voiceSupported)
            Dot4.Visibility = Visibility.Collapsed;

        LoadDemoVideo(ChatDemoMedia,  "chat-translation-demo.mp4");
        LoadDemoVideo(InputDemoMedia, "input-translation-demo.mp4");
        LoadDemoVideo(VoiceDemoMedia, "voice-translation-demo.mp4");
        LoadQualityImage();
    }

    private void GoToStep(int step)
    {
        if (_panels == null) return;

        // install-only: STEP_DONE means "all done, close"
        if (_installOnly && step == STEP_DONE)
        {
            DialogResult = true;
            Close();
            return;
        }

        int leaving = GetCurrentStep();

        if (leaving == STEP_CHAT)        ChatDemoMedia.Stop();
        if (leaving == STEP_INPUT)       InputDemoMedia.Stop();
        if (leaving == STEP_VOICE)       VoiceDemoMedia.Stop();
        if (leaving == STEP_VOICE_SETUP) StopVoiceSetup();

        foreach (var p in _panels) p.Visibility = Visibility.Collapsed;
        _panels[step].Visibility = Visibility.Visible;
        UpdateDots(step);

        if (step == STEP_CHAT)        ChatDemoMedia.Play();
        if (step == STEP_INPUT)       InputDemoMedia.Play();
        if (step == STEP_VOICE)       VoiceDemoMedia.Play();
        if (step == STEP_VOICE_SETUP) StartVoiceSetup();
        if (step == STEP_DONE)        OnboardingCompleted?.Invoke();
    }

    private int GetCurrentStep()
    {
        if (_panels == null) return STEP_WELCOME;
        for (int i = 0; i < _panels.Length; i++)
            if (_panels[i].Visibility == Visibility.Visible) return i;
        return STEP_WELCOME;
    }

    private void UpdateDots(int step)
    {
        if (_dots == null || _installOnly) return;
        var active   = (SolidColorBrush)FindResource("AccentBrush");
        var inactive = (SolidColorBrush)FindResource("TextMutedBrush");
        int activeDot = DotMap[step];
        int dotCount = _voiceSupported ? 5 : 4;
        for (int i = 0; i < dotCount; i++)
            _dots[i].Fill = i == activeDot ? active : inactive;
    }

    // ── Step 0: Welcome ──

    private void OnStep0Start(object sender, RoutedEventArgs e) => GoToStep(STEP_CHAT);

    // ── Step 1: Chat ──

    private void OnChatBack(object sender, RoutedEventArgs e) => GoToStep(STEP_WELCOME);
    private void OnChatNext(object sender, RoutedEventArgs e) => GoToStep(STEP_INPUT);

    private void OnChatMediaEnded(object sender, RoutedEventArgs e)
    {
        ChatDemoMedia.Position = TimeSpan.Zero;
        ChatDemoMedia.Play();
    }

    // ── Step 2: Input ──

    private void OnInputBack(object sender, RoutedEventArgs e) => GoToStep(STEP_CHAT);

    private void OnInputNext(object sender, RoutedEventArgs e)
        => GoToStep(_voiceSupported ? STEP_VOICE : STEP_DONE);

    private void OnInputMediaEnded(object sender, RoutedEventArgs e)
    {
        InputDemoMedia.Position = TimeSpan.Zero;
        InputDemoMedia.Play();
    }

    // ── Step 3: Voice intro ──

    private void OnVoiceBack(object sender, RoutedEventArgs e) => GoToStep(STEP_INPUT);
    private void OnVoiceSkip(object sender, RoutedEventArgs e) => GoToStep(STEP_DONE);
    private void OnVoiceSetup(object sender, RoutedEventArgs e) => GoToStep(STEP_VOICE_SETUP);

    private void OnVoiceMediaEnded(object sender, RoutedEventArgs e)
    {
        VoiceDemoMedia.Position = TimeSpan.Zero;
        VoiceDemoMedia.Play();
    }

    // ── Step 3b: Voice setup ──

    private void StartVoiceSetup()
    {
        ResetVoiceSetupStatus();
        StartInstallGuideVideo();
        LaunchLiveCaptions();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (_, _) => await PollInstallStatusAsync();
        _pollTimer.Start();

        _ = PollInstallStatusAsync();
    }

    private void StopVoiceSetup()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        StopInstallGuideVideo();
    }

    private void ResetVoiceSetupStatus()
    {
        _installStatus["ja-JP"] = false;
        _installStatus["zh-CN"] = false;

        JpStatus.Text = ZhStatus.Text = "확인 중...";
        JpStatus.Foreground = ZhStatus.Foreground =
            (SolidColorBrush)FindResource("TextMutedBrush");
        JpIcon.Text = ZhIcon.Text = "○";
        JpIcon.Foreground = ZhIcon.Foreground =
            (SolidColorBrush)FindResource("TextMutedBrush");
        VoiceSetupNextBtn.IsEnabled = false;
    }

    private async Task PollInstallStatusAsync()
    {
        var jpTask = LanguagePackService.IsInstalledAsync("ja-JP");
        var zhTask = LanguagePackService.IsInstalledAsync("zh-CN");
        await Task.WhenAll(jpTask, zhTask);

        bool jp = jpTask.Result;
        bool zh = zhTask.Result;

        bool jpChanged = jp != _installStatus["ja-JP"];
        bool zhChanged = zh != _installStatus["zh-CN"];
        _installStatus["ja-JP"] = jp;
        _installStatus["zh-CN"] = zh;

        if (jpChanged || zhChanged)
            Logger.Info("Preflight", $"언어팩 상태 변경 — ja-JP={jp}, zh-CN={zh}");

        Dispatcher.Invoke(() => UpdateInstallStatusUI(jp, zh));
    }

    private void UpdateInstallStatusUI(bool jp, bool zh)
    {
        var successColor = (SolidColorBrush)FindResource("TextSuccessBrush");
        var mutedColor   = (SolidColorBrush)FindResource("TextMutedBrush");

        JpIcon.Text       = jp ? "✓" : "○";
        JpIcon.Foreground = jp ? successColor : mutedColor;
        JpStatus.Text     = jp ? "설치 확인됨" : "설치 대기 중";
        JpStatus.Foreground = jp ? successColor : mutedColor;

        ZhIcon.Text       = zh ? "✓" : "○";
        ZhIcon.Foreground = zh ? successColor : mutedColor;
        ZhStatus.Text     = zh ? "설치 확인됨" : "설치 대기 중";
        ZhStatus.Foreground = zh ? successColor : mutedColor;

        VoiceSetupNextBtn.IsEnabled = jp && zh;
    }

    private void OnOpenLiveCaptions(object sender, RoutedEventArgs e) => LaunchLiveCaptions();

    private void OnVoiceSetupBack(object sender, RoutedEventArgs e)
    {
        if (_installOnly) { DialogResult = false; Close(); return; }
        GoToStep(STEP_VOICE);
    }

    private void OnVoiceSetupNext(object sender, RoutedEventArgs e) => GoToStep(STEP_DONE);

    // ── Step 4: Done ──

    private void OnApiUpsellRegister(object sender, RoutedEventArgs e)
    {
        var modal = new ApiConfigModal("", "") { Owner = this };
        if (modal.ShowDialog() == true)
            ApiKeysRegistered?.Invoke(modal.DeepLApiKey, modal.GeminiApiKey);
    }

    private void OnComplete(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // ── Video helpers ──

    private static void LoadDemoVideo(MediaElement media, string filename)
    {
        var path = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", filename);
        if (!IOFile.Exists(path)) return;
        media.Source = new Uri(path, UriKind.Absolute);
    }

    private void StartInstallGuideVideo()
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

    private void StopInstallGuideVideo()
    {
        InstallGuideMedia.Stop();
        InstallGuideMedia.Source = null;
    }

    private void LoadQualityImage()
    {
        var path = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                  "assets", "translation-quality-compare.png");
        if (!IOFile.Exists(path)) return;
        QualityCompareImage.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
    }

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

    // ── Window lifecycle ──

    private void OnWindowDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseApp(object sender, RoutedEventArgs e)
    {
        if (_installOnly) { DialogResult = false; Close(); }
        else Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_installOnly && DialogResult != true)
            Application.Current.Shutdown();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer?.Stop();
        ChatDemoMedia.Stop();
        InputDemoMedia.Stop();
        VoiceDemoMedia.Stop();
        StopInstallGuideVideo();
        base.OnClosed(e);
    }
}
