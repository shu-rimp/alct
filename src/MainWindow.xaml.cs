using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Modals;
using AlctClient.Views.Overlays;
using AlctClient.Views.Windows;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace AlctClient;

public partial class MainWindow : Window
{
    private readonly ChatTranslationOverlay _overlay = new();
    private readonly VoiceTranslationOverlay _voiceOverlay = new();
    private readonly InputTranslationOverlay _inputOverlay = new();
    private readonly QuickSettingsOverlay _langOverlay = new();
    private readonly SettingsWindow _settings = new();
    private readonly LocalOcrService _ocr;
    private readonly TranslationCoordinator _translation;
    private readonly UserSettings _userSettings;

    public MainWindow()
    {
        AssetCache.InvalidateIfVersionChanged();
        InitializeComponent();
        var (serverUrl, deepLApiKey, geminiApiKey, langblyApiKey, myMemoryEmail, voiceEngine, textEngine) = LoadAppSettings();
        _userSettings = UserSettingsService.Load();
        _ocr          = new LocalOcrService();
        _ = Task.Run(() =>  // 첫 핫키 지연 방지용 모델 사전 로드 — 실패해도 첫 OCR 시 다시 시도/안내
        {
            try { _ocr.Warmup(); }
            catch (Exception ex) { Logger.Error("OcrWarmup", ex); }
        });
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
        // 온보딩을 완료하지 않고 닫으면 OnboardingWindow가 Application.Current.Shutdown()을 호출한다.
        // 그 뒤 남은 초기화(트레이 아이콘 등)는 종료 중인 Application에서 실행되어 예외를 던지므로 중단한다.
        // (종료가 진행되면 Application.Current 자체가 null이 될 수 있어 null도 "종료 중"으로 간주)
        if (Application.Current is not { } app || app.Dispatcher.HasShutdownStarted) return;
        _ = CheckForUpdateAsync();
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
            var s = LoadAppSettingsModel();
            var url = string.IsNullOrWhiteSpace(s.ServerUrl) ? fallbackUrl : s.ServerUrl;
            var deepLKey   = DpapiHelper.Decrypt(s.DeepLApiKey   ?? string.Empty);
            var geminiKey  = DpapiHelper.Decrypt(s.GeminiApiKey  ?? string.Empty);
            var langblyKey = DpapiHelper.Decrypt(s.LangblyApiKey ?? string.Empty);
            var myMemoryEmail = s.MyMemoryEmail ?? string.Empty;  // 평문(민감정보 아님)

            return (url, deepLKey, geminiKey, langblyKey, myMemoryEmail,
                TranslationEngineFactory.Parse(s.VoiceTranslationEngine), TranslationEngineFactory.Parse(s.TextTranslationEngine));
        }
        catch { return (fallbackUrl, string.Empty, string.Empty, string.Empty, string.Empty, TranslationEngineFactory.Default, TranslationEngineFactory.Default); }
    }

    private static void MigrateAppSettingsIfNeeded()
    {
        if (File.Exists(_appSettingsPath)) return;
        var legacyPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(legacyPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath)!);
        File.Copy(legacyPath, _appSettingsPath);
    }

    // appsettings.json 스키마. API 키 3종은 DPAPI 암호화 저장, 나머지는 평문. 미설정 필드는 직렬화에서 생략(WhenWritingNull)
    private sealed class AppSettings
    {
        public string? ServerUrl { get; set; }
        public string? DeepLApiKey { get; set; }
        public string? GeminiApiKey { get; set; }
        public string? LangblyApiKey { get; set; }
        public string? MyMemoryEmail { get; set; }
        public string? VoiceTranslationEngine { get; set; }
        public string? TextTranslationEngine { get; set; }
    }

    private static readonly JsonSerializerOptions _appSettingsJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> _encryptedFields = ["DeepLApiKey", "GeminiApiKey", "LangblyApiKey"];

    // 파일이 없으면 빈 모델, 깨졌으면 예외 전파(호출부 catch가 처리: 읽기=폴백, 쓰기=중단+로그)
    private static AppSettings LoadAppSettingsModel()
    {
        if (!File.Exists(_appSettingsPath)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_appSettingsPath), _appSettingsJson) ?? new AppSettings();
    }

    private static void SaveAppSetting(string settingKey, string value)
    {
        try
        {
            // 타입드 모델로 라운드트립 — 기존 머지가 모든 값을 GetString()으로 강제 변환해 비문자열 필드에서 조용히 실패하던 문제 방지
            var settings = LoadAppSettingsModel();
            SetField(settings, settingKey, _encryptedFields.Contains(settingKey) ? DpapiHelper.Encrypt(value) : value);
            Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath)!);
            File.WriteAllText(_appSettingsPath, JsonSerializer.Serialize(settings, _appSettingsJson));
        }
        catch (Exception ex) { Logger.Error("SaveAppSettings", ex); }
    }

    private static void SetField(AppSettings s, string settingKey, string value)
    {
        switch (settingKey)
        {
            case "ServerUrl":              s.ServerUrl = value; break;
            case "DeepLApiKey":            s.DeepLApiKey = value; break;
            case "GeminiApiKey":           s.GeminiApiKey = value; break;
            case "LangblyApiKey":          s.LangblyApiKey = value; break;
            case "MyMemoryEmail":          s.MyMemoryEmail = value; break;
            case "VoiceTranslationEngine": s.VoiceTranslationEngine = value; break;
            case "TextTranslationEngine":  s.TextTranslationEngine = value; break;
            default: throw new ArgumentException($"Unknown setting key: {settingKey}");
        }
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
        _inputOverlay.Close();
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

    private async Task CheckForUpdateAsync()
    {
        var info = await UpdateChecker.CheckAsync();
        if (info is null) return;
        new UpdateModal(info) { Owner = _settings }.ShowDialog();
    }

    private static void SaveDebugCapture(byte[] imageBytes) // 화면캡쳐 확인용
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "capture_debug");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        File.WriteAllBytes(path, imageBytes);
    }
}
