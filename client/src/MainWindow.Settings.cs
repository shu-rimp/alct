using AlctClient.Core;
using AlctClient.Utils;

namespace AlctClient;

public partial class MainWindow
{
    private TrayIconManager? _tray;
    private bool _updatingLang;
    private bool _updatingCaption;

    private void InitTray()
    {
        _tray = new TrayIconManager();
        _tray.OpenSettingsRequested += () => Dispatcher.Invoke(() =>
        {
            _settings.Show();
            _settings.Activate();
        });
        _tray.ExitRequested += () => Dispatcher.Invoke(ShutdownApp);
    }

    private void InitSettings()
    {
        _settings.SourceLangChanged += async lang =>
        {
            _langOverlay.SetLanguage(lang);
            if (_updatingLang) return;
            await HandleSourceLangChangedAsync(lang);
        };

        _settings.CaptionModeChanged += async enabled =>
        {
            _langOverlay.SetCaptionMode(enabled);
            if (_updatingCaption) return;
            await HandleCaptionModeChangedAsync(enabled);
        };

        _langOverlay.LanguageChanged += async lang =>
        {
            _updatingLang = true;
            Dispatcher.Invoke(() => _settings.SetSourceLang(lang));
            _updatingLang = false;
            await HandleSourceLangChangedAsync(lang);
        };

        _langOverlay.CaptionModeChanged += async enabled =>
        {
            _updatingCaption = true;
            Dispatcher.Invoke(() => _settings.SetCaptionMode(enabled));
            _updatingCaption = false;
            await HandleCaptionModeChangedAsync(enabled);
        };

        _settings.ShowLanguageOverlayChanged += show =>
        {
            if (show) _langOverlay.Show();
            else _langOverlay.Hide();
            _userSettings.ShowLanguageOverlay = show;
            UserSettingsService.Save(_userSettings);
        };

        _settings.OverlayPositionEditRequested += () => EnterEditMode();
        _settings.SetCaptureRegionRequested    += () => EnterCaptureRegionEditMode();

        _settings.CaptureRegionModeChanged += isCustom =>
        {
            if (!isCustom)
            {
                _userSettings.UseCustomCaptureRegion = false;
                _screenCapture.SetCaptureRegion(ScreenCaptureService.GetDefaultCaptureRegion());
                UserSettingsService.Save(_userSettings);
            }
        };

        _settings.DeepLApiKeyChanged += key =>
        {
            _translationService = new DeepLTranslationService(key);
            SaveAppSetting("DeepLApiKey", key);
        };

        _settings.TranslationEngineChanged += isDeepL =>
            SaveAppSetting("TranslationEngine", isDeepL ? "DeepL" : "LibreTranslate");
    }
}
