using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Windows;
using System.Diagnostics;
using System.Management;

namespace AlctClient;

public partial class MainWindow
{
    private const int CAPTION_CONTEXT_SIZE = 5; // 번역 컨텍스트로 보낼 직전 발화 수

    private readonly CaptionMonitorService _captionMonitor = new();
    private readonly SemaphoreSlim _captionLock = new(1, 1);
    private readonly SemaphoreSlim _translateQueue = new(1, 1);
    private readonly Queue<string> _captionContext = new();
    private ManagementEventWatcher? _liveCaptionsWatcher;

    private void InitOcrHandler()
    {
        _ocrClient.OcrTextReceived += async (normalizedText, rawText) =>
        {
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translation.TextService.TranslateToKoreanAsync(normalizedText, sourceLang);
                _overlay.ShowTranslation(translation, rawText);
            }
            catch (Exception ex) { Logger.Error("OcrTranslation", ex); }
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
                // 일일 한도류는 재개 시각까지, 영구 소진(DeepL 평생 한도)은 사실상 무기한 차단 + 1회 안내
                _translation.BlockVoiceQuotaUntil(ex.RetryAtUtc);
                var msg = ex.RetryAtUtc - DateTime.UtcNow > TimeSpan.FromDays(30)
                    ? ex.Message  // 재개 시각이 없는 영구 소진 — 사유만
                    : $"{ex.Message} — {ex.RetryAtUtc.ToLocalTime():HH:mm} 이후 다시 사용할 수 있어요.";
                _voiceOverlay.ShowTranslation(msg);
                Logger.Info("CaptionTranslation", $"{ex.Message} — {ex.RetryAtUtc:u}까지 음성 번역 차단");
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
        _userSettings.SourceLang = lang;
        UserSettingsService.Save(_userSettings);
        ClearCaptionContext();

        if (!await _captionLock.WaitAsync(0)) return;
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
        finally { _captionLock.Release(); }
    }

    private async Task HandleCaptionModeChangedAsync(bool enabled)
    {
        if (enabled && !await AllLanguagePacksInstalledAsync())
        {
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
        Logger.Info("Preflight", $"언어팩 설치 상태 — ja-JP={jp.Result}, zh-CN={zh.Result}");
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
