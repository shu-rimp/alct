using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Modals;
using System.Net.Http;
using Clipboard = System.Windows.Clipboard;

namespace AlctClient;

public partial class MainWindow
{
    private HotkeyManager? _hotkeyManager;
    private ScreenCaptureService _screenCapture = new();
    private readonly SemaphoreSlim _ocrLock = new(1, 1);
    private bool _screenCaptureLogged;

    private void InitHotkeys()
    {
        if (!IsLoaded || !IsVisible) return;
        if (_userSettings.UseCustomCaptureRegion && _userSettings.CustomCaptureWidth > 0)
            _screenCapture.SetCaptureRegion(new System.Drawing.Rectangle(
                _userSettings.CustomCaptureX, _userSettings.CustomCaptureY,
                _userSettings.CustomCaptureWidth, _userSettings.CustomCaptureHeight));
        else
            _screenCapture.SetCaptureRegion(ScreenCaptureService.GetDefaultCaptureRegion(GetSelectedScreen()));

        _hotkeyManager = new HotkeyManager(this);
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        _hotkeyManager.InputTranslationHotkeyPressed += OnInputTranslationHotkeyPressed;
        _hotkeyManager.Register(_userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey);
        _hotkeyManager.RegisterInputTranslation(_userSettings.InputHotkeyModifiers, _userSettings.InputHotkeyVKey);

        _settings.SetCaptureHotkeyLabel(HotkeyManager.FormatHotkey(_userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey));
        _settings.SetInputHotkeyLabel(HotkeyManager.FormatHotkey(_userSettings.InputHotkeyModifiers, _userSettings.InputHotkeyVKey));
    }

    internal void RebindCaptureHotkey() => RebindHotkey(
        "채팅창 번역",
        _userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey,
        _userSettings.InputHotkeyModifiers,   _userSettings.InputHotkeyVKey,
        (mods, vkey) => { _userSettings.CaptureHotkeyModifiers = mods; _userSettings.CaptureHotkeyVKey = vkey; },
        _settings.SetCaptureHotkeyLabel);

    internal void RebindInputHotkey() => RebindHotkey(
        "입력창 번역",
        _userSettings.InputHotkeyModifiers,   _userSettings.InputHotkeyVKey,
        _userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey,
        (mods, vkey) => { _userSettings.InputHotkeyModifiers = mods; _userSettings.InputHotkeyVKey = vkey; },
        _settings.SetInputHotkeyLabel);

    private void RebindHotkey(
        string title,
        uint curMods, uint curVKey,
        uint otherMods, uint otherVKey,
        Action<uint, uint> applySettings,
        Action<string> updateLabel)
    {
        _hotkeyManager?.Unregister();
        var modal = new HotkeyRecorderModal(title, curMods, curVKey, otherMods, otherVKey) { Owner = _settings };
        if (modal.ShowDialog() == true)
        {
            applySettings(modal.Modifiers, modal.VKey);
            UserSettingsService.Save(_userSettings);
            updateLabel(HotkeyManager.FormatHotkey(modal.Modifiers, modal.VKey));
        }
        _hotkeyManager?.Reregister(_userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey);
        _hotkeyManager?.ReregisterInputTranslation(_userSettings.InputHotkeyModifiers, _userSettings.InputHotkeyVKey);
    }

    private void OnHotkeyPressed()
    {
        if (!_ocrLock.Wait(0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var overlayVisible = Dispatcher.Invoke(() =>
                {
                    var v = _overlay.IsVisible;
                    if (v) _overlay.Hide();
                    return v;
                });

                var imageBytes = _screenCapture.CaptureRegionAsPng();

                Dispatcher.Invoke(() => { if (overlayVisible) _overlay.Show(); });

                if (!_screenCaptureLogged)
                {
                    _screenCaptureLogged = true;
                    Logger.Info("Preflight", "화면 캡처: GDI 사용 가능");
                }
                // SaveDebugCapture(imageBytes); 실제 캡쳐이미지 확인(디버그용)
                await _ocrClient.SendImageAsync(imageBytes);
            }
            catch (HttpRequestException ex)
            {
                Logger.Error("OcrRequest", ex);
                _overlay.ShowNotice("서버에 일시적으로 접속할 수 없어요. 잠시 후 다시 시도해주세요.");
            }
            catch (Exception ex)
            {
                if (!_screenCaptureLogged) { _screenCaptureLogged = true; Logger.Info("Preflight", "화면 캡처: GDI 사용 불가"); }
                Logger.Error("OcrCapture", ex);
                _overlay.ShowNotice("이 기기는 화면 캡처를 지원하지 않아 채팅 번역을 사용할 수 없어요.");
            }
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
                var translation = await _textTranslationService.TranslateFromKoreanAsync(text, sourceLang);
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
