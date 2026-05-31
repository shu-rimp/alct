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
    private readonly SettingsWindow _settings = new();
    private readonly WebSocketClient _wsClient = new(SERVER_URL);
    private readonly CancellationTokenSource _wsCts = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings.SourceLangChanged += lang => _ = _wsClient.SendSettingsAsync(lang);
        _settings.Show();

        _wsClient.MessageReceived += text => _overlay.ShowTranslation(text);
        _wsClient.InputTranslationReceived += OnInputTranslationReceived;
        _wsClient.ConnectionChanged += connected =>
        {
            if (connected)
            {
                // _settings.SourceLang accesses WPF controls — must be read on UI thread
                var lang = Dispatcher.Invoke(() => _settings.SourceLang);
                _ = _wsClient.SendSettingsAsync(lang);
            }
        };
        _ = _wsClient.ConnectAsync(_wsCts.Token);

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
        _wsCts.Cancel();
        _wsClient.Dispose();
        _hotkeyManager?.Dispose();
    }
}
