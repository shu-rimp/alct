using AlctClient.Core;
using AlctClient.Views.Windows;

namespace AlctClient;

public partial class MainWindow
{
    private void RunOnboardingIfNeeded()
    {
        if (_userSettings.OnboardingComplete) return;

        // DEBUG: uncomment to test install flow even when packs are installed
        // LanguagePackService.ForceUninstalled = true;

        var onboarding = new OnboardingWindow(
            _userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey,
            _userSettings.InputHotkeyModifiers,   _userSettings.InputHotkeyVKey);

        onboarding.ApiKeysRegistered  += OnOnboardingApiKeysRegistered;
        onboarding.OnboardingCompleted += OnOnboardingReachedDone;

        onboarding.ShowDialog();
    }

    private void OnOnboardingReachedDone()
    {
        _userSettings.OnboardingComplete = true;
        UserSettingsService.Save(_userSettings);
    }

    private void OnOnboardingApiKeysRegistered(string deepLKey, string geminiKey, string langblyKey)
    {
        if (!string.IsNullOrEmpty(deepLKey))
        {
            _deepLKey = deepLKey;
            SaveAppSetting("DeepLApiKey", deepLKey);
            if (_voiceEngine == TranslationEngine.DeepL)
                _voiceTranslationService = TranslationEngineFactory.Create(TranslationEngine.DeepL, deepLKey);
            if (_textEngine == TranslationEngine.DeepL)
                _textTranslationService = TranslationEngineFactory.Create(TranslationEngine.DeepL, deepLKey);
            _settings.SetDeepLApiKey(deepLKey);
        }
        if (!string.IsNullOrEmpty(geminiKey))
        {
            _geminiKey = geminiKey;
            SaveAppSetting("GeminiApiKey", geminiKey);
            if (_voiceEngine == TranslationEngine.Gemini)
                _voiceTranslationService = TranslationEngineFactory.Create(TranslationEngine.Gemini, geminiKey);
            if (_textEngine == TranslationEngine.Gemini)
                _textTranslationService = TranslationEngineFactory.Create(TranslationEngine.Gemini, geminiKey);
            _settings.SetGeminiApiKey(geminiKey);
        }
        if (!string.IsNullOrEmpty(langblyKey))
        {
            _langblyKey = langblyKey;
            SaveAppSetting("LangblyApiKey", langblyKey);
            if (_voiceEngine == TranslationEngine.Langbly)
                _voiceTranslationService = TranslationEngineFactory.Create(TranslationEngine.Langbly, langblyKey);
            if (_textEngine == TranslationEngine.Langbly)
                _textTranslationService = TranslationEngineFactory.Create(TranslationEngine.Langbly, langblyKey);
            _settings.SetLangblyApiKey(langblyKey);
        }
    }
}
