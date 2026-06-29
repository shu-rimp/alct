using AlctClient.Core;
using AlctClient.Views.Windows;

namespace AlctClient;

public partial class MainWindow
{
    private void RunOnboardingIfNeeded()
    {
        if (_userSettings.OnboardingComplete) return;

        var onboarding = new OnboardingWindow(
            _userSettings.CaptureHotkeyModifiers, _userSettings.CaptureHotkeyVKey,
            _userSettings.InputHotkeyModifiers,   _userSettings.InputHotkeyVKey);

        onboarding.ApiKeysRegistered  += OnOnboardingApiKeysRegistered;
        onboarding.OnboardingCompleted += OnOnboardingReachedDone;

        onboarding.ShowDialog();

        if (_userSettings.OnboardingComplete && _userSettings.ShowLanguageOverlay)
            _langOverlay.Show();
    }

    private void OnOnboardingReachedDone()
    {
        _userSettings.OnboardingComplete = true;
        UserSettingsService.Save(_userSettings);
    }

    private void OnOnboardingApiKeysRegistered(string deepLKey, string geminiKey, string langblyKey, string myMemoryEmail)
    {
        if (!string.IsNullOrEmpty(deepLKey))
        {
            _translation.UpdateCredential(TranslationEngine.DeepL, deepLKey);
            SaveAppSetting("DeepLApiKey", deepLKey);
            _settings.SetDeepLApiKey(deepLKey);
        }
        if (!string.IsNullOrEmpty(geminiKey))
        {
            _translation.UpdateCredential(TranslationEngine.Gemini, geminiKey);
            SaveAppSetting("GeminiApiKey", geminiKey);
            _settings.SetGeminiApiKey(geminiKey);
        }
        if (!string.IsNullOrEmpty(langblyKey))
        {
            _translation.UpdateCredential(TranslationEngine.Langbly, langblyKey);
            SaveAppSetting("LangblyApiKey", langblyKey);
            _settings.SetLangblyApiKey(langblyKey);
        }
        if (!string.IsNullOrEmpty(myMemoryEmail))
        {
            _translation.UpdateCredential(TranslationEngine.MyMemory, myMemoryEmail);
            SaveAppSetting("MyMemoryEmail", myMemoryEmail);
            _settings.SetMyMemoryEmail(myMemoryEmail);
        }
    }
}
