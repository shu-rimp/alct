using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Overlays;
using AlctClient.Views.Windows;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace AlctClient;

public partial class MainWindow : Window
{
    private const uint DEFAULT_HOTKEY_MODIFIERS = (uint)HotkeyModifiers.Ctrl;
    private const uint DEFAULT_HOTKEY_VKEY = 0x54;        // T — 화면 캡처 번역
    private const uint DEFAULT_INPUT_HOTKEY_VKEY = 0x47;  // G — 선택 텍스트 번역

    private readonly ChatTranslationOverlay _overlay = new();
    private readonly VoiceTranslationOverlay _voiceOverlay = new();
    private readonly QuickSettingsOverlay _langOverlay = new();
    private readonly SettingsWindow _settings = new();
    private readonly OcrHttpClient _ocrClient;
    private ITranslationService _translationService;
    private readonly UserSettings _userSettings;

    public MainWindow()
    {
        InitializeComponent();
        var (serverUrl, deepLApiKey, translationEngine) = LoadAppSettings();
        _userSettings = UserSettingsService.Load();
        _ocrClient = new OcrHttpClient(serverUrl);
        _translationService = new DeepLTranslationService(deepLApiKey);

        _settings.SetDeepLApiKey(deepLApiKey);
        _settings.SetSourceLang(_userSettings.SourceLang);
        _settings.SetCaptionMode(_userSettings.CaptionModeEnabled);
        _settings.SetTranslationEngine(translationEngine == "DeepL");
        _settings.SetShowLanguageOverlay(_userSettings.ShowLanguageOverlay);
        _settings.SetCaptureRegionMode(_userSettings.UseCustomCaptureRegion);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_userSettings.CaptionModeEnabled)
            WindowsApiHelper.SetLiveCaptionsVisible(true);
        _settings.Show();
        InitTray();
        InitSettings();
        InitOverlays();
        InitOcrCaption();
        InitHotkeys();
        if (_userSettings.CaptionModeEnabled)
            _ = InitCaptionModeAsync();
    }

    private static (string serverUrl, string deepLApiKey, string translationEngine) LoadAppSettings()
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
            var engine = doc.RootElement.TryGetProperty("TranslationEngine", out var engineProp)
                ? engineProp.GetString() ?? "LibreTranslate" : "LibreTranslate";
            return (url, key, engine);
        }
        catch { return (fallbackUrl, string.Empty, "LibreTranslate"); }
    }

    private static void SaveAppSetting(string settingKey, string value)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var existing = File.Exists(path) ? File.ReadAllText(path) : "{}";
            using var doc = JsonDocument.Parse(existing);
            var dict = new Dictionary<string, string?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.GetString();
            dict[settingKey] = value;
            File.WriteAllText(path, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logger.Error("SaveAppSettings", ex); }
    }

    private void ShutdownApp()
    {
        _settings.AllowClose();
        Application.Current.Shutdown();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _settings.AllowClose();
        _langOverlay.Close();
        _voiceOverlay.Close();
        _editPanel.Close();
        _captureRegionOverlay.Close();
        if (_userSettings.CaptionModeEnabled)
            WindowsApiHelper.StopLiveCaptions();
        _hotkeyManager?.Dispose();
        _captionMonitor.Dispose();
        _tray?.Dispose();
    }

    private static void SaveDebugCapture(byte[] imageBytes) // 화면캡쳐 확인용
    {
        var path = Path.Combine(AppContext.BaseDirectory, "capture_debug.png");
        File.WriteAllBytes(path, imageBytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
