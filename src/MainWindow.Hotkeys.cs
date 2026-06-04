using AlctClient.Core;
using AlctClient.Utils;
using System.Net.Http;
using Clipboard = System.Windows.Clipboard;

namespace AlctClient;

public partial class MainWindow
{
    private HotkeyManager? _hotkeyManager;
    private ScreenCaptureService _screenCapture = new();
    private readonly SemaphoreSlim _ocrLock = new(1, 1);

    private void InitHotkeys()
    {
        if (_userSettings.UseCustomCaptureRegion && _userSettings.CustomCaptureWidth > 0)
            _screenCapture.SetCaptureRegion(new System.Drawing.Rectangle(
                _userSettings.CustomCaptureX, _userSettings.CustomCaptureY,
                _userSettings.CustomCaptureWidth, _userSettings.CustomCaptureHeight));
        else
            _screenCapture.SetCaptureRegion(ScreenCaptureService.GetDefaultCaptureRegion(GetSelectedScreen()));

        _hotkeyManager = new HotkeyManager(this);
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        _hotkeyManager.InputTranslationHotkeyPressed += OnInputTranslationHotkeyPressed;
        _hotkeyManager.Register(DEFAULT_HOTKEY_MODIFIERS, DEFAULT_HOTKEY_VKEY);
        _hotkeyManager.RegisterInputTranslation(DEFAULT_HOTKEY_MODIFIERS, DEFAULT_INPUT_HOTKEY_VKEY);
    }

    private void OnHotkeyPressed()
    {
        if (!_ocrLock.Wait(0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var imageBytes = _screenCapture.CaptureRegionAsPng();
                // SaveDebugCapture(imageBytes); 실제 캡쳐이미지 확인(디버그용)
                await _ocrClient.SendImageAsync(imageBytes);
            }
            catch (HttpRequestException ex) { Logger.Error("OcrRequest", ex); }
            catch (Exception ex) { Logger.Error("OcrCapture", ex); }
            finally { _ocrLock.Release(); }
        });
    }

    private void OnInputTranslationHotkeyPressed()
    {
        WindowsApiHelper.SimulateCopy();
        _ = Task.Run(async () =>
        {
            try
            {
                var text = await WaitForClipboardTextAsync();
                if (string.IsNullOrWhiteSpace(text)) return;

                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translationService.TranslateFromKoreanAsync(text, sourceLang);
                Dispatcher.Invoke(() => Clipboard.SetText(translation));
                await Task.Delay(50);
                WindowsApiHelper.SimulatePaste();
            }
            catch (Exception ex) { Logger.Error("InputTranslation", ex); }
        });
    }

    private async Task<string?> WaitForClipboardTextAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(30);
            var text = Dispatcher.Invoke(() =>
                Clipboard.ContainsText() ? Clipboard.GetText() : null);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }
}
