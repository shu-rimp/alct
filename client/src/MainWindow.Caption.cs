using AlctClient.Core;
using AlctClient.Utils;
using System.Diagnostics;
using System.Management;

namespace AlctClient;

public partial class MainWindow
{
    private readonly CaptionMonitorService _captionMonitor = new();
    private readonly SemaphoreSlim _captionLock = new(1, 1);
    private readonly SemaphoreSlim _translateQueue = new(1, 1); // 번역 순서 보장 — 발화당 1개씩 순차 처리
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
            await _translateQueue.WaitAsync();
            try
            {
                _voiceOverlay.ShowOriginalPinned(text);
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _voiceTranslationService.TranslateToKoreanAsync(text, sourceLang);
                _voiceOverlay.ShowTranslation(translation);
            }
            catch (Exception ex) { Logger.Error("CaptionTranslation", ex); }
            finally { _translateQueue.Release(); }
        };
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
            }
        }
        catch (Exception ex) { Logger.Error("CaptionMode", ex); }
        finally { _captionLock.Release(); }
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
