using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Overlays;
using AlctClient.Views.Windows;
using Microsoft.Win32;
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
    private readonly TranslationCoordinator _translation;
    private readonly UserSettings _userSettings;

    public MainWindow()
    {
        InitializeComponent();
        var (serverUrl, deepLApiKey, geminiApiKey, langblyApiKey, myMemoryEmail, voiceEngine, textEngine) = LoadAppSettings();
        _userSettings = UserSettingsService.Load();
        _ocrClient    = new OcrHttpClient(serverUrl);
        _ = GlossaryService.Instance.RefreshFromServerAsync(serverUrl);
        _translation  = new TranslationCoordinator(
            voiceEngine, textEngine, deepLApiKey, geminiApiKey, langblyApiKey, myMemoryEmail);

        _settings.SetDeepLApiKey(deepLApiKey);
        _settings.SetGeminiApiKey(geminiApiKey);
        _settings.SetLangblyApiKey(langblyApiKey);
        _settings.SetMyMemoryEmail(myMemoryEmail);
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
        LogPreflightEnvironment();
        bool liveCaption = WindowsApiHelper.IsLiveCaptionSupported();
        if (_userSettings.CaptionModeEnabled && liveCaption)
            WindowsApiHelper.SetLiveCaptionsVisible(true);
        InitSettings();
        InitOverlays();
        InitOcrHandler();
        InitVoiceHandler();
        RunOnboardingIfNeeded();
        InitTray();
        InitHotkeys();
        _settings.Show();
        _settings.SetVoiceSupported(liveCaption);
        _settings.SetMonitorIndex(_userSettings.MonitorIndex);
        if (_userSettings.CaptionModeEnabled && liveCaption)
            _ = InitCaptionModeAsync();
    }

    private static readonly string _appSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ALCT", "appsettings.json");

    private static (string serverUrl, string deepLKey, string geminiKey, string langblyKey, string myMemoryEmail, TranslationEngine voiceEngine, TranslationEngine textEngine) LoadAppSettings()
    {
        var fallbackUrl = BuildConstants.SERVER_URL;
        try
        {
            MigrateAppSettingsIfNeeded();
            var path = _appSettingsPath;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var customUrl  = root.TryGetProperty("ServerUrl", out var p1) ? p1.GetString() : null;
            var url        = string.IsNullOrWhiteSpace(customUrl) ? fallbackUrl : customUrl;
            var deepLKey   = DpapiHelper.Decrypt(root.TryGetProperty("DeepLApiKey",   out var p2) ? p2.GetString() ?? string.Empty : string.Empty);
            var geminiKey  = DpapiHelper.Decrypt(root.TryGetProperty("GeminiApiKey",  out var p3) ? p3.GetString() ?? string.Empty : string.Empty);
            var langblyKey = DpapiHelper.Decrypt(root.TryGetProperty("LangblyApiKey", out var p4) ? p4.GetString() ?? string.Empty : string.Empty);
            var myMemoryEmail = root.TryGetProperty("MyMemoryEmail", out var p5) ? p5.GetString() ?? string.Empty : string.Empty;  // 평문(민감정보 아님)

            // 구버전 단일 엔진 설정 폴백
            string? legacyEngine = root.TryGetProperty("TranslationEngine", out var leg) ? leg.GetString() : null;
            string? voiceStr = root.TryGetProperty("VoiceTranslationEngine", out var ve) ? ve.GetString() : legacyEngine;
            string? textStr  = root.TryGetProperty("TextTranslationEngine",  out var te) ? te.GetString()
                             : root.TryGetProperty("OcrTranslationEngine",   out var oe) ? oe.GetString() : legacyEngine;

            return (url, deepLKey, geminiKey, langblyKey, myMemoryEmail, TranslationEngineFactory.Parse(voiceStr), TranslationEngineFactory.Parse(textStr));
        }
        catch { return (fallbackUrl, string.Empty, string.Empty, string.Empty, string.Empty, TranslationEngine.MyMemory, TranslationEngine.MyMemory); }
    }

    private static void MigrateAppSettingsIfNeeded()
    {
        if (File.Exists(_appSettingsPath)) return;
        var legacyPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(legacyPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath)!);
        File.Copy(legacyPath, _appSettingsPath);
    }

    private static readonly HashSet<string> _encryptedFields = ["DeepLApiKey", "GeminiApiKey", "LangblyApiKey"];

    private static void SaveAppSetting(string settingKey, string value)
    {
        try
        {
            var storedValue = _encryptedFields.Contains(settingKey) ? DpapiHelper.Encrypt(value) : value;
            var path = _appSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var existing = File.Exists(path) ? File.ReadAllText(path) : "{}";
            using var doc = JsonDocument.Parse(existing);
            var dict = new Dictionary<string, string?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.GetString();
            dict[settingKey] = storedValue;
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

    private static void LogPreflightEnvironment()
    {
        bool liveCaption = WindowsApiHelper.IsLiveCaptionSupported();
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string build   = key?.GetValue("CurrentBuild")    as string ?? "unknown";
        string display = key?.GetValue("DisplayVersion")  as string ?? "unknown";
        Logger.Info("Preflight", $"Windows build={build} ({display}), LiveCaption={liveCaption}");
    }

    private static void SaveDebugCapture(byte[] imageBytes) // 화면캡쳐 확인용
    {
        var path = Path.Combine(AppContext.BaseDirectory, "capture_debug.png");
        File.WriteAllBytes(path, imageBytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
