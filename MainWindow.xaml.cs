using AlctClient.Core;
using AlctClient.Overlay;
using AlctClient.Utils;
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
    private static readonly string SERVER_URL = LoadServerUrl();
    private static readonly string CAPTION_SERVER_URL = SERVER_URL + "/caption";

    private static string LoadServerUrl()
    {
        const string fallback = "ws://localhost:8000/ws";
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("ServerUrl").GetString() ?? fallback;
        }
        catch { return fallback; }
    }

    private HotkeyManager? _hotkeyManager;
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly TranslationOverlay _overlay = new();
    private readonly TranslationOverlay _captionOverlay = new();
    private readonly SettingsWindow _settings = new();
    private readonly WebSocketClient _wsClient = new(SERVER_URL);
    private readonly WebSocketClient _captionWsClient = new(CAPTION_SERVER_URL);
    private readonly CancellationTokenSource _wsCts = new();
    private readonly CancellationTokenSource _captionWsCts = new();
    private readonly CaptionMonitorService _captionMonitor;

    public MainWindow()
    {
        InitializeComponent();
        _captionMonitor = new CaptionMonitorService();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsApiHelper.SetLiveCaptionsVisible(true);
        _settings.SourceLangChanged += lang => _ = _wsClient.SendSettingsAsync(lang);
        _settings.CaptionModeChanged += enabled =>
        {
            WindowsApiHelper.SetLiveCaptionsVisible(!enabled);
            if (enabled) _captionMonitor.Start();
            else _captionMonitor.Stop();
        };
        _captionMonitor.CaptionStabilized += text => _ = _captionWsClient.SendCaptionTextAsync(text);
        _settings.Show();

        _wsClient.MessageReceived += text => _overlay.ShowTranslation(text);
        _wsClient.InputTranslationReceived += OnInputTranslationReceived;
        _wsClient.ConnectionChanged += connected =>
        {
            if (connected)
            {
                var lang = Dispatcher.Invoke(() => _settings.SourceLang);
                _ = _wsClient.SendSettingsAsync(lang);
            }
        };
        _ = _wsClient.ConnectAsync(_wsCts.Token);

        _captionWsClient.MessageReceived += text => _captionOverlay.ShowAtLiveCaptions(text);
        _ = _captionWsClient.ConnectAsync(_captionWsCts.Token);

        _hotkeyManager = new HotkeyManager(this);
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        _hotkeyManager.InputTranslationHotkeyPressed += OnInputTranslationHotkeyPressed;
        _hotkeyManager.Register(DEFAULT_HOTKEY_MODIFIERS, DEFAULT_HOTKEY_VKEY);
        _hotkeyManager.RegisterInputTranslation(DEFAULT_HOTKEY_MODIFIERS, DEFAULT_INPUT_HOTKEY_VKEY);
    }

    private void OnHotkeyPressed()
    {
        _ = Task.Run(async () =>
        {
            var imageBytes = _screenCapture.CaptureRegionAsPng();
            // SaveDebugCapture(imageBytes);
            await _wsClient.SendImageAsync(imageBytes);
        });
    }

    private void OnInputTranslationHotkeyPressed()
    {
        WindowsApiHelper.SimulateCopy();
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var text = Dispatcher.Invoke(() =>
                Clipboard.ContainsText() ? Clipboard.GetText() : null);
            if (string.IsNullOrWhiteSpace(text)) return;
            await _wsClient.SendInputTranslationRequestAsync(text);
        });
    }

    private void OnInputTranslationReceived(string translatedText)
    {
        _ = Task.Run(async () =>
        {
            Dispatcher.Invoke(() => Clipboard.SetText(translatedText));
            await Task.Delay(50);
            WindowsApiHelper.SimulatePaste();
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
        WindowsApiHelper.SetLiveCaptionsVisible(true);
        _wsCts.Cancel();
        _captionWsCts.Cancel();
        _wsClient.Dispose();
        _captionWsClient.Dispose();
        _hotkeyManager?.Dispose();
        _captionMonitor.Dispose();
    }
}
