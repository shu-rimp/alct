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

        _settings.ChangeCaptureHotkeyRequested += RebindCaptureHotkey;
        _settings.ChangeInputHotkeyRequested   += RebindInputHotkey;

        _settings.OverlayPositionEditRequested += () => EnterEditMode();
        _settings.SetCaptureRegionRequested    += () => EnterCaptureRegionEditMode();

        _settings.CaptureRegionModeChanged += isCustom =>
        {
            if (!isCustom)
            {
                _userSettings.UseCustomCaptureRegion = false;
                _screenCapture.SetCaptureRegion(ScreenCaptureService.GetDefaultCaptureRegion(GetSelectedScreen()));
                UserSettingsService.Save(_userSettings);
            }
        };

        _settings.MonitorIndexChanged += index =>
        {
            var oldScreen = GetSelectedScreen();
            _userSettings.MonitorIndex = index;
            var newScreen = GetSelectedScreen();
            TranslateAllOverlaysToMonitor(oldScreen, newScreen);
            UpdateCaptureRegionForMonitor(oldScreen, newScreen);
            SaveOverlayPositions();
            UserSettingsService.Save(_userSettings);
        };

        _settings.DeepLApiKeyChanged += key =>
        {
            _deepLKey = key;
            if (_voiceEngine == TranslationEngine.DeepL)
                _voiceTranslationService = TranslationEngineFactory.Create(TranslationEngine.DeepL, key);
            if (_textEngine == TranslationEngine.DeepL)
                _textTranslationService = TranslationEngineFactory.Create(TranslationEngine.DeepL, key);
            SaveAppSetting("DeepLApiKey", key);
        };

        _settings.GeminiApiKeyChanged += key =>
        {
            _geminiKey = key;
            if (_voiceEngine == TranslationEngine.Gemini)
                _voiceTranslationService = TranslationEngineFactory.Create(TranslationEngine.Gemini, key);
            if (_textEngine == TranslationEngine.Gemini)
                _textTranslationService = TranslationEngineFactory.Create(TranslationEngine.Gemini, key);
            SaveAppSetting("GeminiApiKey", key);
        };

        _settings.LangblyApiKeyChanged += key =>
        {
            _langblyKey = key;
            if (_voiceEngine == TranslationEngine.Langbly)
                _voiceTranslationService = TranslationEngineFactory.Create(TranslationEngine.Langbly, key);
            if (_textEngine == TranslationEngine.Langbly)
                _textTranslationService = TranslationEngineFactory.Create(TranslationEngine.Langbly, key);
            SaveAppSetting("LangblyApiKey", key);
        };

        _settings.VoiceEngineChanged += engine =>
        {
            _voiceEngine = engine;
            _voiceTranslationService = TranslationEngineFactory.Create(engine, GetApiKey(engine));
            SaveAppSetting("VoiceTranslationEngine", engine.ToString());
        };

        _settings.TextEngineChanged += engine =>
        {
            _textEngine = engine;
            _textTranslationService = TranslationEngineFactory.Create(engine, GetApiKey(engine));
            SaveAppSetting("TextTranslationEngine", engine.ToString());
        };
    }
}
