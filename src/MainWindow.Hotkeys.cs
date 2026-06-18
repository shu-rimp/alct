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
                    Logger.Info("Preflight", "Screen capture: GDI available");
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
                if (!_screenCaptureLogged) { _screenCaptureLogged = true; Logger.Info("Preflight", "Screen capture: GDI unavailable"); }
                Logger.Error("OcrCapture", ex);
                _overlay.ShowNotice("이 기기는 화면 캡처를 지원하지 않아 채팅 번역을 사용할 수 없어요.");
            }
            finally { _ocrLock.Release(); }
        });
    }

    private void OnInputTranslationHotkeyPressed()
    {
        // 사용자가 입력한 채팅을 선택·복사(Shift+Home&Ctrl+C, read 주입)로 가져온 뒤, 번역문을 클립보드에 올려두기만 한다.
        // 게임에 글자를 써넣는 자동 붙여넣기(Ctrl+V, write 주입)는 하지 않음 — 사용자가 직접 Ctrl+V로 붙여넣는다.
        // 번역 완료 전에 채팅창을 닫아도 클립보드에 남아 나중에 붙여넣을 수 있다는 부수적인 이점이 생김.
        // 복사 시뮬레이션이 사용자의 원본 클립보드를 덮어쓰기 전에 백업 (UI/STA 스레드)
        var clipboardBackup = WindowsApiHelper.BackupClipboard();

        WindowsApiHelper.SimulateSelectToLineStart();
        WindowsApiHelper.SimulateCopy();
        _ = Task.Run(async () =>
        {
            var success = false;
            try
            {
                var text = await WaitForClipboardTextAsync();
                if (string.IsNullOrWhiteSpace(text)) return;

                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translation.TextService.TranslateFromKoreanAsync(text, sourceLang);
                Dispatcher.Invoke(() =>
                {
                    Clipboard.SetText(translation);
                    _overlay.ShowNotice("번역 완료! Ctrl+V로 붙여넣으세요.");
                });
                success = true;
            }
            catch (Exception ex) { Logger.Error("InputTranslation", ex); }
            finally
            {
                // 성공 시 번역문을 클립보드에 남겨 사용자가 직접 붙여넣게 한다.
                // 실패·빈 입력이면 복사 단계가 덮어쓴 원본 클립보드를 되돌린다.
                if (!success)
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
