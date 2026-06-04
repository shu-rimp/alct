using AlctClient.Core;
using AlctClient.Utils;
using System.Diagnostics;

namespace AlctClient;

public partial class MainWindow
{
    private readonly CaptionMonitorService _captionMonitor = new();
    private readonly SemaphoreSlim _captionLock = new(1, 1);

    private void InitOcrCaption()
    {
        _ocrClient.OcrTextReceived += async (normalizedText, rawText) =>
        {
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translationService.TranslateToKoreanAsync(normalizedText, sourceLang);
                _overlay.ShowTranslation(translation, rawText);
            }
            catch (Exception ex) { Logger.Error("OcrTranslation", ex); }
        };

        _captionMonitor.CaptionStabilized += async text =>
        {
            try
            {
                var sourceLang = Dispatcher.Invoke(() => _settings.SourceLang);
                var translation = await _translationService.TranslateToKoreanAsync(text, sourceLang);
                _voiceOverlay.ShowTranslation(translation, text);
            }
            catch (Exception ex) { Logger.Error("CaptionTranslation", ex); }
        };
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
            }
            else
            {
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
        }
        catch (Exception ex) { Logger.Error("CaptionModeInit", ex); }
        finally { _captionLock.Release(); }
    }
}
