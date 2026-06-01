using AlctClient.Core;
using AlctClient.Overlay;
using AlctClient.Utils;
using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using Clipboard = System.Windows.Clipboard;

namespace AlctClient;

public partial class MainWindow : Window
{
    private const uint DEFAULT_HOTKEY_MODIFIERS = (uint)HotkeyModifiers.Ctrl;
    private const uint DEFAULT_HOTKEY_VKEY = 0x54;        // T — 화면 캡처 번역
    private const uint DEFAULT_INPUT_HOTKEY_VKEY = 0x47;  // G — 선택 텍스트 번역

    private HotkeyManager? _hotkeyManager;
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly TranslationOverlay _overlay = new();
    private readonly TranslationOverlay _captionOverlay = new();
    private readonly SettingsWindow _settings = new();
    private readonly OcrHttpClient _ocrClient;
    private readonly CaptionMonitorService _captionMonitor;
    private ITranslationService _translationService;
    private readonly UserSettings _userSettings;

    public MainWindow()
    {
        InitializeComponent();
        var (serverUrl, deepLApiKey) = LoadAppSettings();
        _userSettings = UserSettingsService.Load();
        _ocrClient = new OcrHttpClient(serverUrl);
        _translationService = new DeepLTranslationService(deepLApiKey);
        _captionMonitor = new CaptionMonitorService();

        _settings.SetDeepLApiKey(deepLApiKey);
        _settings.SetSourceLang(_userSettings.SourceLang);
        _settings.SetCaptionMode(_userSettings.CaptionModeEnabled);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private static (string serverUrl, string deepLApiKey) LoadAppSettings()
    {
        const string fallbackUrl = "http://localhost:8000";
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var url = doc.RootElement.TryGetProperty("ServerUrl", out var urlProp)
                ? urlProp.GetString() ?? fallbackUrl : fallbackUrl;
            var key = doc.RootElement.TryGetProperty("DeepLApiKey", out var keyProp)
                ? keyProp.GetString() ?? string.Empty : string.Empty;
            return (url, key);
        }
        catch { return (fallbackUrl, string.Empty); }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsApiHelper.SetLiveCaptionsVisible(true);
        _settings.Show();

        _settings.SourceLangChanged += lang =>
        {
            _userSettings.SourceLang = lang;
            UserSettingsService.Save(_userSettings);
        };

        _settings.CaptionModeChanged += async enabled =>
        {
            try
            {
                if (enabled)
                {
                    await WindowsApiHelper.StartLiveCaptionsAsync();
                    WindowsApiHelper.SetLiveCaptionsVisible(false);
                    _captionMonitor.Start();
                }
                else
                {
                    _captionMonitor.Stop();
                    WindowsApiHelper.StopLiveCaptions();
                }
                _userSettings.CaptionModeEnabled = enabled;
                UserSettingsService.Save(_userSettings);
            }
            catch (Exception ex) { Logger.Error("CaptionMode", ex); }
        };

        _settings.DeepLApiKeyChanged += key => _translationService = new DeepLTranslationService(key);

        if (_userSettings.CaptionModeEnabled)
            _ = InitCaptionModeAsync();

        _ocrClient.OcrTextReceived += async normalizedText =>
        {
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translationService.TranslateToKoreanAsync(normalizedText, sourceLang);
                _overlay.ShowTranslation(translation);
            }
            catch (Exception ex) { Logger.Error("OcrTranslation", ex); }
        };

        _captionMonitor.CaptionStabilized += async text =>
        {
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translationService.TranslateToKoreanAsync(text, sourceLang);
                _captionOverlay.ShowAtLiveCaptions(translation);
            }
            catch (Exception ex) { Logger.Error("CaptionTranslation", ex); }
        };

        _hotkeyManager = new HotkeyManager(this);
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        _hotkeyManager.InputTranslationHotkeyPressed += OnInputTranslationHotkeyPressed;
        _hotkeyManager.Register(DEFAULT_HOTKEY_MODIFIERS, DEFAULT_HOTKEY_VKEY);
        _hotkeyManager.RegisterInputTranslation(DEFAULT_HOTKEY_MODIFIERS, DEFAULT_INPUT_HOTKEY_VKEY);
    }

    private async Task InitCaptionModeAsync()
    {
        try
        {
            await WindowsApiHelper.StartLiveCaptionsAsync();
            WindowsApiHelper.SetLiveCaptionsVisible(false);
            _captionMonitor.Start();
        }
        catch (Exception ex) { Logger.Error("CaptionModeInit", ex); }
    }

    private void OnHotkeyPressed()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var imageBytes = _screenCapture.CaptureRegionAsPng();
                // SaveDebugCapture(imageBytes);
                await _ocrClient.SendImageAsync(imageBytes);
            }
            catch (HttpRequestException ex) { Logger.Error("OcrRequest", ex); }
            catch (Exception ex) { Logger.Error("OcrCapture", ex); }
        });
    }

    private void OnInputTranslationHotkeyPressed()
    {
        WindowsApiHelper.SimulateCopy();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(50);
                var (text, sourceLang) = Dispatcher.Invoke(() => (
                    Clipboard.ContainsText() ? Clipboard.GetText() : null,
                    _settings.SourceLang));
                if (string.IsNullOrWhiteSpace(text)) return;

                var translation = await _translationService.TranslateFromKoreanAsync(text, sourceLang);
                Dispatcher.Invoke(() => Clipboard.SetText(translation));
                await Task.Delay(50);
                WindowsApiHelper.SimulatePaste();
            }
            catch (Exception ex) { Logger.Error("InputTranslation", ex); }
        });
    }

    private static void SaveDebugCapture(byte[] imageBytes) // 화면캡쳐 확인용
    {
        var path = Path.Combine(AppContext.BaseDirectory, "capture_debug.png");
        File.WriteAllBytes(path, imageBytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_userSettings.CaptionModeEnabled)
            WindowsApiHelper.StopLiveCaptions();
        _hotkeyManager?.Dispose();
        _captionMonitor.Dispose();
    }
}
