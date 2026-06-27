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
    private System.Drawing.Rectangle _activeCaptureRegion;  // 이번 캡처에 사용한 영역(일반=저장값, 길게누르기=드래그 1회). 오버레이 배치 기준.
    private bool _screenCaptureLogged;
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
        _hotkeyManager.LongPressHotkeyPressed += OnLongPressHotkeyPressed;
        _hotkeyManager.InputTranslationHotkeyPressed += OnInputTranslationHotkeyPressed;
        _hotkeyManager.ClipboardUpdated += OnClipboardUpdated;
        _hotkeyManager.DismissHotkeyPressed += () => _overlay.HideNow();
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

    // 짧게 누름 — 저장된(또는 자동) 캡처 영역으로 번역.
    private void OnHotkeyPressed() => RunCapture(_screenCapture.GetCaptureRegion());

    // 길게 누름 — 드래그로 영역을 직접 선택(1회성). 저장된 캡처 영역은 바꾸지 않는다.
    private void OnLongPressHotkeyPressed() => Dispatcher.Invoke(() =>
    {
        if (_overlay.IsVisible) _overlay.HideNow();  // 기존 결과/안내 치우고 선택 화면을 깔끔히
        _dragSelectOverlay.ShowForScreen(GetSelectedScreen());
    });

    // 주어진 영역을 1회 캡처→OCR→번역. region을 저장하지 않으므로 드래그 선택은 자동으로 1회성이 된다.
    private void RunCapture(System.Drawing.Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0) return;
        if (!_ocrLock.Wait(0)) return;
        _activeCaptureRegion = region;
        _ = Task.Run(async () =>
        {
            try
            {
                // 오버레이가 이전 결과로 캡처 화면을 가리지 않도록 캡처 직전에 숨긴다.
                Dispatcher.Invoke(() => { if (_overlay.IsVisible) _overlay.Hide(); });

                using var bitmap = _screenCapture.CaptureRegion(region);

                // 캡처가 끝난 뒤 스피너를 띄운다(캡처 이미지에 안 잡히도록). 결과/오류는 ShowTranslations/ShowNotice가 내림.
                _overlay.ShowLoading(region);

                if (!_screenCaptureLogged)
                {
                    _screenCaptureLogged = true;
                    Logger.Info("Preflight", "Screen capture: GDI available");
                }
                // 로컬 OCR — 결과는 OcrRegionsReceived(InitOcrHandler)로 흐른다. 서버 호출/rate-limit 없음.
                await _ocr.RecognizeAsync(bitmap);
            }
            catch (Exception ex)
            {
                // 캡처 실패(GDI 미지원) 또는 OCR 추론/모델 로드 실패 — 안내만, 상세는 로그로.
                if (!_screenCaptureLogged) { _screenCaptureLogged = true; Logger.Info("Preflight", "Screen capture: GDI unavailable"); }
                Logger.Error("OcrCapture", ex);
                _overlay.ShowNotice("채팅 번역 중 문제가 발생했어요. 잠시 후 다시 시도해주세요.");
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
            catch (TranslationRateLimitException ex)
            {
                Logger.Info("InputTranslation", $"Translation blocked until {ex.RetryAtUtc:u} — reason: {ex.Message}");
                _inputOverlay.ShowNotice(FormatQuotaNotice(ex));  // ShowNotice가 내부에서 디스패치
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
