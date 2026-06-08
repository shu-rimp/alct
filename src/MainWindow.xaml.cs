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
    private readonly ChatTranslationOverlay _overlay = new();
    private readonly VoiceTranslationOverlay _voiceOverlay = new();
    private readonly QuickSettingsOverlay _langOverlay = new();
    private readonly SettingsWindow _settings = new();
    private readonly OcrHttpClient _ocrClient;
    private ITranslationService _voiceTranslationService;
    private ITranslationService _textTranslationService;
    private readonly UserSettings _userSettings;
    private string _deepLKey  = string.Empty;
    private string _geminiKey = string.Empty;
    private TranslationEngine _voiceEngine = TranslationEngine.MyMemory;
    private TranslationEngine _textEngine  = TranslationEngine.MyMemory;

    public MainWindow()
    {
        InitializeComponent();
        var (serverUrl, deepLApiKey, geminiApiKey, voiceEngine, textEngine) = LoadAppSettings();
        _userSettings = UserSettingsService.Load();
        _ocrClient    = new OcrHttpClient(serverUrl);
        _deepLKey     = deepLApiKey;
        _geminiKey    = geminiApiKey;
        _voiceEngine  = voiceEngine;
        _textEngine   = textEngine;
        _voiceTranslationService = TranslationEngineFactory.Create(voiceEngine,  GetApiKey(voiceEngine));
        _textTranslationService  = TranslationEngineFactory.Create(textEngine,   GetApiKey(textEngine));

        _settings.SetDeepLApiKey(deepLApiKey);
        _settings.SetGeminiApiKey(geminiApiKey);
        _settings.SetSourceLang(_userSettings.SourceLang);
        _settings.SetCaptionMode(_userSettings.CaptionModeEnabled);
        _settings.SetVoiceEngine(voiceEngine);
        _settings.SetTextEngine(textEngine);
        _settings.SetShowLanguageOverlay(_userSettings.ShowLanguageOverlay);
        _settings.SetCaptureRegionMode(_userSettings.UseCustomCaptureRegion);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_userSettings.CaptionModeEnabled)
            WindowsApiHelper.SetLiveCaptionsVisible(true);
        InitSettings();
        InitOverlays();
        InitOcrHandler();
        InitVoiceHandler();
        RunOnboardingIfNeeded();
        InitTray();
        InitHotkeys();
        _settings.Show();
        _settings.SetMonitorIndex(_userSettings.MonitorIndex);
        if (_userSettings.CaptionModeEnabled)
            _ = InitCaptionModeAsync();
    }

    private static (string serverUrl, string deepLKey, string geminiKey, TranslationEngine voiceEngine, TranslationEngine textEngine) LoadAppSettings()
    {
        const string fallbackUrl = "http://localhost:8000";
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var url       = root.TryGetProperty("ServerUrl",    out var p1) ? p1.GetString() ?? fallbackUrl  : fallbackUrl;
            var deepLKey  = root.TryGetProperty("DeepLApiKey",  out var p2) ? p2.GetString() ?? string.Empty : string.Empty;
            var geminiKey = root.TryGetProperty("GeminiApiKey", out var p3) ? p3.GetString() ?? string.Empty : string.Empty;

            // 구버전 단일 엔진 설정 폴백
            string? legacyEngine = root.TryGetProperty("TranslationEngine", out var leg) ? leg.GetString() : null;
            string? voiceStr = root.TryGetProperty("VoiceTranslationEngine", out var ve) ? ve.GetString() : legacyEngine;
            string? textStr  = root.TryGetProperty("TextTranslationEngine",  out var te) ? te.GetString()
                             : root.TryGetProperty("OcrTranslationEngine",   out var oe) ? oe.GetString() : legacyEngine;

            return (url, deepLKey, geminiKey, TranslationEngineFactory.Parse(voiceStr), TranslationEngineFactory.Parse(textStr));
        }
        catch { return (fallbackUrl, string.Empty, string.Empty, TranslationEngine.MyMemory, TranslationEngine.MyMemory); }
    }

    private string GetApiKey(TranslationEngine engine) => engine switch
    {
        TranslationEngine.DeepL  => _deepLKey,
        TranslationEngine.Gemini => _geminiKey,
        _                        => string.Empty,
    };

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
        StopLiveCaptionsWatcher();
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
