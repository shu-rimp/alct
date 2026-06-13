using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Windows;
using System.Diagnostics;
using System.Management;

namespace AlctClient;

public partial class MainWindow
{
    private const int CAPTION_CONTEXT_SIZE = 4; // 번역 컨텍스트로 보낼 직전 발화 수

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
                var translation = await _textTranslationService.TranslateToKoreanAsync(normalizedText, sourceLang);
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
            _voiceOverlay.ShowOriginal(text);
            var context = BuildCaptionContext(text);
            await _translateQueue.WaitAsync();
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _voiceTranslationService.TranslateToKoreanAsync(text, sourceLang, context);
                _voiceOverlay.ShowTranslation(translation);
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
            WindowsApiHelper.SetLiveCaptionsVisible(true);
            _captionMonitor.Start();
            StartLiveCaptionsWatcher();
        }
        catch (Exception ex) { Logger.Error("CaptionModeInit", ex); }
        finally { _captionLock.Release(); }
    }
}
