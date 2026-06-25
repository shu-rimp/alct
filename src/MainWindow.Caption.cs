using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Windows;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace AlctClient;

public partial class MainWindow
{
    private const int CAPTION_CONTEXT_SIZE = 3; // 번역 컨텍스트로 보낼 직전 발화 수

    private readonly CaptionMonitorService _captionMonitor = new();
    private readonly SemaphoreSlim _captionLock = new(1, 1);
    private readonly SemaphoreSlim _translateQueue = new(1, 1);
    private readonly Queue<string> _captionContext = new();
    private ManagementEventWatcher? _liveCaptionsWatcher;

    // 채팅 입력창 노이즈("채팅:KR", ":KR", "KR") — 3인 위치에선 캡쳐 하단에 입력창까지 들어와 번역 노이즈가 됨.
    // 항상 단독 줄 + 대문자 KR로만 인식되므로, 소문자 kr이나 "KR player" 같은 실제 채팅은 보존됨(대소문자 구분).
    private static readonly Regex ChatInputPromptRegex =
        new(@"^\s*(채팅)?\s*[:：]?\s*KR\s*$", RegexOptions.Compiled);

    private static string StripChatInputPrompt(string text) =>
        string.Join("\n", text.Split('\n').Where(line => !ChatInputPromptRegex.IsMatch(line))).Trim();

    private void InitOcrHandler()
    {
        _ocr.OcrTextReceived += async (normalizedText, rawText) =>
        {
            try
            {
                var cleaned = StripChatInputPrompt(normalizedText);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    // 인식된 텍스트가 없거나(빈 OCR), 내 입력창 프롬프트만 잡힌 경우 
                    _overlay.ShowNotice("번역할 채팅을 찾지 못했어요.");
                    return;
                }
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translation.TextService.TranslateToKoreanAsync(cleaned, sourceLang);
                _overlay.ShowTranslation(translation, StripChatInputPrompt(rawText));
            }
            catch (TranslationRateLimitException ex)
            {
                Logger.Info("OcrTranslation", $"Translation blocked until {ex.RetryAtUtc:u} — reason: {ex.Message}");
                _overlay.ShowNotice(FormatQuotaNotice(ex));
            }
            catch (Exception ex)
            {
                Logger.Error("OcrTranslation", ex);
                _overlay.ShowNotice("번역에 실패했어요. 잠시 후 다시 시도해주세요."); // 빈 화면 대신 안내
            }
            finally { _overlay.HideLoading(); } // 성공/안내 시엔 이미 스피너가 꺼졌고, 그 외 잔여 스피너만 정리
        };
    }

    private void InitVoiceHandler()
    {
        _captionMonitor.CaptionUpdating += delta =>
            _voiceOverlay.ShowPending(delta);

        _captionMonitor.CaptionStabilized += async text =>
        {
            // 번역 엔진 한도 초과 후 재개 시각까지: 요청도 오버레이 갱신도 하지 않음
            // (계속 보내봐야 매번 실패해 에러 로그만 쌓이고, 번역 안 된 원문만 덮어씀)
            if (_translation.IsVoiceQuotaBlocked) return;

            _voiceOverlay.ShowOriginal(text);
            var context = BuildCaptionContext(text);
            await _translateQueue.WaitAsync();
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translation.VoiceService.TranslateToKoreanAsync(text, sourceLang, context);
                _voiceOverlay.ShowTranslation(translation);
            }
            catch (TranslationRateLimitException ex) when (ex.RetryAtUtc - DateTime.UtcNow <= TimeSpan.FromMinutes(10))
            {
                // 분당 제한처럼 곧 풀리는 단기 차단 — 안내도 차단도 없이 원문 그대로 통과(더 자연스러움). 곧 회복됨
                _voiceOverlay.ShowTranslation(text);
            }
            catch (TranslationRateLimitException ex)
            {
                // 일일 한도류는 재개 시각까지, 영구 소진(DeepL 무료 요금제의 일회성 한도)은 사실상 무기한 차단 + 1회 안내
                _translation.BlockVoiceQuotaUntil(ex.RetryAtUtc);
                _voiceOverlay.ShowTranslation(FormatQuotaNotice(ex));
                Logger.Info("CaptionTranslation", $"Voice translation blocked until {ex.RetryAtUtc:u} — reason: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error("CaptionTranslation", ex);
                // 번역 실패 시 원문으로 채움 — 비워두면 다음 번역이 이 발화의 원문과 짝지어짐
                _voiceOverlay.ShowTranslation(text);
            }
            finally { _translateQueue.Release(); }
        };
    }

    // 한도 초과 안내 문구 — 음성/채팅/입력 번역이 공유. 재개 시각이 사실상 없는(영구 소진) 경우 사유만, 그 외엔 재개 시각 안내
    private static string FormatQuotaNotice(TranslationRateLimitException ex) =>
        ex.RetryAtUtc - DateTime.UtcNow > TimeSpan.FromDays(30)
            ? ex.Message  // 재개 시각이 없는 영구 소진 — 사유만
            : $"{ex.Message} — {ex.RetryAtUtc.ToLocalTime():HH:mm} 이후 다시 사용할 수 있어요.";

    // 직전 발화들을 컨텍스트 문자열로 반환하고 현재 발화를 버퍼에 추가
    // 짧고 맥락 없는 게임 대화의 번역 정확도를 높이기 위한 롤링 컨텍스트
    private string? BuildCaptionContext(string text)
    {
        lock (_captionContext)
        {
            var context = _captionContext.Count > 0 ? string.Join("\n", _captionContext) : null;
            _captionContext.Enqueue(text);
            while (_captionContext.Count > CAPTION_CONTEXT_SIZE)
                _captionContext.Dequeue();
            return context;
        }
    }

    // 언어 변경/캡션 종료 시 이전 언어·세션의 발화가 컨텍스트로 섞이지 않도록 비움
    private void ClearCaptionContext()
    {
        lock (_captionContext) _captionContext.Clear();
    }

    private void StartLiveCaptionsWatcher()
    {
        StopLiveCaptionsWatcher();
        var query = new WqlEventQuery(
            "SELECT * FROM __InstanceDeletionEvent WITHIN 1 " +
            "WHERE TargetInstance ISA 'Win32_Process' " +
            "AND TargetInstance.Name = 'LiveCaptions.exe'");
        _liveCaptionsWatcher = new ManagementEventWatcher(query);
        _liveCaptionsWatcher.EventArrived += OnLiveCaptionsProcessExited;
        _liveCaptionsWatcher.Start();
    }

    internal void StopLiveCaptionsWatcher()
    {
        if (_liveCaptionsWatcher is null) return;
        _liveCaptionsWatcher.Stop();
        _liveCaptionsWatcher.Dispose();
        _liveCaptionsWatcher = null;
    }

    private async void OnLiveCaptionsProcessExited(object sender, EventArrivedEventArgs e)
    {
        if (!_userSettings.CaptionModeEnabled) return;
        if (!await _captionLock.WaitAsync(0)) return;
        try
        {
            _captionMonitor.Stop();
            WindowsApiHelper.SetLiveCaptionsLanguage(_userSettings.SourceLang);
            await WindowsApiHelper.StartLiveCaptionsAsync();
            await WindowsApiHelper.WaitForLiveCaptionsWindowAsync();
            if (!_userSettings.CaptionModeEnabled)
            {
                WindowsApiHelper.StopLiveCaptions();
                return;
            }
            WindowsApiHelper.SetLiveCaptionsVisible(false);
            _captionMonitor.Start();
        }
        catch (Exception ex) { Logger.Error("LiveCaptionsRestart", ex); }
        finally { _captionLock.Release(); }
    }

    private async Task HandleSourceLangChangedAsync(string lang)
    {
        // 새 PC에서 음성 번역 ON시 첫 언어 전환이 길어져(라이브 캡션 워밍업) 라디오 버튼이 먹통처럼 보임.
        // 무거운 작업 전 busy 상태(스피너+버튼 잠금)를 켜고 렌더 패스 한 번을 양보해, 피드백이 항상 먼저 그려지게 한다.
        bool restartNeeded = Process.GetProcessesByName("LiveCaptions").Length > 0;
        if (restartNeeded)
        {
            _langOverlay.SetBusy(true);
            await Dispatcher.Yield(DispatcherPriority.Background); // SetBusy + IsChecked가 그려진 뒤 진행
        }

        _userSettings.SourceLang = lang;
        UserSettingsService.Save(_userSettings);
        ClearCaptionContext();

        if (!await _captionLock.WaitAsync(0)) { _langOverlay.SetBusy(false); return; }
        try
        {
            if (Process.GetProcessesByName("LiveCaptions").Length > 0)
            {
                _captionMonitor.Stop();
                await WindowsApiHelper.StopLiveCaptionsAsync();
                WindowsApiHelper.SetLiveCaptionsLanguage(lang);
                await WindowsApiHelper.StartLiveCaptionsAsync();
                await WindowsApiHelper.WaitForLiveCaptionsWindowAsync();
                WindowsApiHelper.SetLiveCaptionsVisible(false);
                _captionMonitor.Start();
            }
            else
            {
                WindowsApiHelper.SetLiveCaptionsLanguage(lang);
            }
        }
        catch (Exception ex) { Logger.Error("CaptionLangChange", ex); }
        finally
        {
            _langOverlay.SetBusy(false);   // 정상 상태 보장
            _captionLock.Release();
        }
    }

    private async Task HandleCaptionModeChangedAsync(bool enabled, bool fromQuickOverlay = false)
    {
        if (enabled && !await AllLanguagePacksInstalledAsync())
        {
            // 음성팩 미설치 상태에서 빠른 설정 오버레이의 음성 번역 토글을 킨 경우: 게임의 몰입을 해치는 팝업창 대신 오버레이로 안내하고 토글을 되돌린다.
            // 입력 번역 오버레이 재사용
            if (fromQuickOverlay)
            {
                _updatingCaption = true;
                Dispatcher.Invoke(() =>
                {
                    _settings.SetCaptionMode(false);
                    _langOverlay.SetCaptionMode(false);
                    _inputOverlay.ShowNotice("먼저 언어팩 설치가 필요해요. 설정에서 설치해주세요.");
                });
                _updatingCaption = false;
                return;
            }

            bool confirmed = await Dispatcher.InvokeAsync(() =>
            {
                var window = OnboardingWindow.ForLanguagePackInstall();
                window.Owner = _settings.IsVisible ? _settings : null;
                return window.ShowDialog() == true;
            });

            if (!confirmed)
            {
                _updatingCaption = true;
                Dispatcher.Invoke(() =>
                {
                    _settings.SetCaptionMode(false);
                    _langOverlay.SetCaptionMode(false);
                });
                _updatingCaption = false;
                return;
            }
        }

        _userSettings.CaptionModeEnabled = enabled;
        UserSettingsService.Save(_userSettings);

        if (!await _captionLock.WaitAsync(0)) return;
        try
        {
            if (enabled)
            {
                var lang = Dispatcher.Invoke(() => _settings.SourceLang);
                WindowsApiHelper.SetLiveCaptionsLanguage(lang);
                await WindowsApiHelper.StartLiveCaptionsAsync();
                await WindowsApiHelper.WaitForLiveCaptionsWindowAsync();
                WindowsApiHelper.SetLiveCaptionsVisible(false);
                _captionMonitor.Start();
                StartLiveCaptionsWatcher();
            }
            else
            {
                StopLiveCaptionsWatcher();
                _captionMonitor.Stop();
                WindowsApiHelper.StopLiveCaptions();
                ClearCaptionContext();
            }
        }
        catch (Exception ex) { Logger.Error("CaptionMode", ex); }
        finally { _captionLock.Release(); }
    }

    private static async Task<bool> AllLanguagePacksInstalledAsync()
    {
        var jp = LanguagePackService.IsInstalledAsync("ja-JP");
        var zh = LanguagePackService.IsInstalledAsync("zh-CN");
        await Task.WhenAll(jp, zh);
        Logger.Info("Preflight", $"Language pack install status — ja-JP={jp.Result}, zh-CN={zh.Result}");
        return jp.Result && zh.Result;
    }

    private async Task InitCaptionModeAsync()
    {
        if (!await _captionLock.WaitAsync(0)) return;
        try
        {
            await Task.Delay(500); // 앱 초기화 완료 대기
            if (Process.GetProcessesByName("LiveCaptions").Length > 0)
                await WindowsApiHelper.StopLiveCaptionsAsync();
            WindowsApiHelper.SetLiveCaptionsLanguage(_userSettings.SourceLang);
            await WindowsApiHelper.StartLiveCaptionsAsync();
            await WindowsApiHelper.WaitForLiveCaptionsWindowAsync();
            WindowsApiHelper.SetLiveCaptionsVisible(false);
            _captionMonitor.Start();
            StartLiveCaptionsWatcher();
        }
        catch (Exception ex) { Logger.Error("CaptionModeInit", ex); }
        finally { _captionLock.Release(); }
    }
}
