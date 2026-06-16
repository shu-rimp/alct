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
    private DateTime _ocrBlockedUntil = DateTime.MinValue;  // 서버 429(Retry-After) 동안 OCR 요청 억제

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
        if (DateTime.UtcNow < _ocrBlockedUntil)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((_ocrBlockedUntil - DateTime.UtcNow).TotalSeconds));
            _overlay.ShowNotice($"요청이 많아 잠시 제한됐어요. {seconds}초 후 다시 시도해주세요.");
            return;
        }
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
            catch (OcrRequestException ex)
            {
                // 서버가 코드별로 구분한 거부(400/403/413/429/503/504) — 안내만, 연결 장애 아님
                if (ex.RetryAtUtc is { } until) _ocrBlockedUntil = until;
                Logger.Info("OcrRequest", $"{ex.Message} (HTTP {ex.StatusCode})");
                _overlay.ShowNotice(ex.Message);
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
        // 복사 시뮬레이션이 사용자의 원본 클립보드를 덮어쓰기 전에 백업 (UI/STA 스레드)
        var clipboardBackup = WindowsApiHelper.BackupClipboard();

        WindowsApiHelper.SimulateSelectToLineStart();
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
                await Task.Delay(100); // 대상 앱이 붙여넣기를 소화할 시간 확보 후 원복
            }
            catch (Exception ex) { Logger.Error("InputTranslation", ex); }
            finally
            {
                // 성공·실패·빈 입력 모두 원본 클립보드로 복원 (임시 복사본·번역문 잔류 방지)
                Dispatcher.Invoke(() => WindowsApiHelper.RestoreClipboard(clipboardBackup));
            }
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
