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

        onboarding.ApiKeyConfirmed    += OnOnboardingApiKeyConfirmed;
        onboarding.OverlayCheckEntered += ShowOnboardingOverlays;
        onboarding.OverlayCheckExited  += HideOnboardingOverlays;

        onboarding.ShowDialog();

        if (onboarding.DialogResult == true)
        {
            _userSettings.OnboardingComplete = true;
            UserSettingsService.Save(_userSettings);
        }
    }

    private void OnOnboardingApiKeyConfirmed(string key)
    {
        SaveAppSetting("DeepLApiKey", key);
        _translationService = new DeepLTranslationService(key);
        _settings.SetDeepLApiKey(key);
    }

    private void ShowOnboardingOverlays()
    {
        var screen = GetSelectedScreen();
        _overlay.MoveToMonitor(screen);
        _voiceOverlay.MoveToMonitor(screen);
        _overlay.SetEditMode(true);
        _voiceOverlay.SetEditMode(true);
    }

    private void HideOnboardingOverlays()
    {
        _overlay.SetEditMode(false);
        _voiceOverlay.SetEditMode(false);
        SaveOverlayPositions();
        UserSettingsService.Save(_userSettings);
    }
}
