using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Modals;
using System.Net.Http;

namespace AlctClient;

public partial class MainWindow
{
    private HotkeyManager? _hotkeyManager;
    private ScreenCaptureService _screenCapture = new();
    private readonly SemaphoreSlim _ocrLock = new(1, 1);
    private bool _screenCaptureLogged;
    private DateTime _ocrBlockedUntil = DateTime.MinValue;  // 서버 429(Retry-After) 동안 OCR 요청 억제
    private string? _lastInputTranslation;  // 직전 입력 번역 결과 — 복사를 누락하고 핫키만 누른 경우(클립보드에 이전 번역문이 그대로) 감지용
    private const int MAX_INPUT_CHARS = 70;  // 입력 번역 최대 글자수 — 게임 입력창 한도(알파벳·한글 동일 63자)에 약간의 여유. 브라우저 등에서 대량 텍스트가 번역 요청으로 새는 것 방지

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
        _hotkeyManager.ClipboardUpdated += OnClipboardUpdated;
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

                // 캡처가 끝난 뒤 스피너를 띄운다(캡처 이미지에 안 잡히도록). 결과/오류는 ShowTranslation/ShowNotice가 내림.
                _overlay.ShowLoading();

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
        // 가상 키보드 입력 주입 방식 모두 제거.
        // 또한, 게임은 독자적인 렌더링(예: DirectX)을 사용하는 경우가 많으므로, UI Automation의 Selection으로는 텍스트를 읽어올 수 없음.
        // 텍스트 선택(Shift+Home)·복사(Ctrl+C)·붙여넣기(Ctrl+V)를 전부 사용자의 물리 입력으로 변경한다.
        // 대신 저하된 ux는 ui로 보강한다.(Overlays/InputTransltationOverlay)
        
        // 사용자가 이미 클립보드에 올려둔 텍스트를 읽기만 해서 번역문으로 바꿔준다.
        _inputOverlay.SetCaptureAnchor(_screenCapture.GetCaptureRegion());
        var text = ClipboardHelper.TryGetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _inputOverlay.ShowNotice("문장을 복사한 뒤 다시 눌러주세요.");
            return;
        }

        // 복사를 깜빡하고 핫키만 누를경우: 직전 번역 텍스트와 비교.
        if (text == _lastInputTranslation)
        {
            _inputOverlay.ShowNotice("새 문장을 복사한 뒤 다시 눌러주세요.");
            return;
        }

        // 글자수 제한: 게임 입력창 한도를 넘는 텍스트(브라우저 등에서 복사한 대량 텍스트)는 번역 요청 전에 차단한다.
        var trimmed = text.Trim();
        if (trimmed.Length > MAX_INPUT_CHARS)
        {
            _inputOverlay.ShowNotice($"{MAX_INPUT_CHARS}자 이하만 번역할 수 있어요.");
            return;
        }

        _inputOverlay.ShowLoading();
        _ = Task.Run(async () =>
        {
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translation.TextService.TranslateFromKoreanAsync(trimmed, sourceLang);
                Dispatcher.Invoke(() =>
                {
                    // 성공 시 번역문을 클립보드에 남겨 사용자가 직접 Ctrl+V로 붙여넣게 한다.
                    // 클립보드 쓰기 전에 기록 — 우리 쓰기가 일으킬 클립보드 알림을 "준비 완료" 힌트에서 걸러내기 위함.
                    _lastInputTranslation = translation;
                    ClipboardHelper.TrySetText(translation);
                    _inputOverlay.ShowResult(translation);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("InputTranslation", ex);
                _inputOverlay.ShowNotice("번역에 실패했어요. 잠시 후 다시 시도해주세요.");
            }
        });
    }

    // 사용자가 무언가 복사하면 "번역 준비 완료"를 띄워 단축키 사용을 유도한다.
    // 우리가 직접 클립보드에 올린 번역문(직전 결과)이 일으킨 알림은 제외 — 자기 쓰기로 인한 재표시 방지.
    private void OnClipboardUpdated()
    {
        var text = ClipboardHelper.TryGetText();
        if (string.IsNullOrWhiteSpace(text) || text == _lastInputTranslation) return;
        _inputOverlay.SetCaptureAnchor(_screenCapture.GetCaptureRegion());
        _inputOverlay.ShowReady();
    }
}
