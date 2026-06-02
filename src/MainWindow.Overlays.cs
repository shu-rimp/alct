using AlctClient.Core;
using AlctClient.Views.Overlays;

namespace AlctClient;

public partial class MainWindow
{
    private readonly EditPanelOverlay _editPanel = new();

    // 편집 모드 진입 시 복원용 스냅샷
    private double _snapVoiceLeft, _snapVoiceTop, _snapVoiceWidth;
    private double _snapTextLeft,  _snapTextTop,  _snapTextWidth;
    private double _snapOpacity;

    private void InitOverlays()
    {
        _langOverlay.SetLanguage(_userSettings.SourceLang);
        _langOverlay.SetCaptionMode(_userSettings.CaptionModeEnabled);
        _langOverlay.SetOpacity(_userSettings.OverlayOpacity);
        if (_userSettings.ShowLanguageOverlay) _langOverlay.Show();

        _voiceOverlay.LoadBounds(_userSettings.VoiceOverlayLeft, _userSettings.VoiceOverlayTop, _userSettings.VoiceOverlayWidth);
        _overlay.LoadBounds(_userSettings.TextOverlayLeft, _userSettings.TextOverlayTop, _userSettings.TextOverlayWidth);

        _overlay.SetOpacity(_userSettings.OverlayOpacity);
        _voiceOverlay.SetOpacity(_userSettings.OverlayOpacity);
        _langOverlay.SetOpacity(_userSettings.OverlayOpacity);

        _editPanel.OpacityChanged += opacity =>
        {
            _overlay.SetOpacity(opacity);
            _voiceOverlay.SetOpacity(opacity);
            _langOverlay.SetOpacity(opacity);
            _userSettings.OverlayOpacity = opacity;
        };
        _editPanel.SaveRequested   += () => ExitEditMode(save: true);
        _editPanel.CancelRequested += () => ExitEditMode(save: false);
    }

    private void EnterEditMode()
    {
        _snapVoiceLeft  = _voiceOverlay.Left;
        _snapVoiceTop   = _voiceOverlay.Top;
        _snapVoiceWidth = _voiceOverlay.Width;
        _snapTextLeft   = _overlay.Left;
        _snapTextTop    = _overlay.Top;
        _snapTextWidth  = _overlay.Width;
        _snapOpacity    = _userSettings.OverlayOpacity;

        _voiceOverlay.SetEditMode(true);
        _overlay.SetEditMode(true);
        _editPanel.SetOpacity(_userSettings.OverlayOpacity);
        _editPanel.Show();
        _settings.Hide();
    }

    private void ExitEditMode(bool save)
    {
        _voiceOverlay.SetEditMode(false);
        _overlay.SetEditMode(false);
        _editPanel.Hide();

        if (save)
        {
            _userSettings.VoiceOverlayLeft  = _voiceOverlay.Left;
            _userSettings.VoiceOverlayTop   = _voiceOverlay.Top;
            _userSettings.VoiceOverlayWidth = _voiceOverlay.Width;
            _userSettings.TextOverlayLeft   = _overlay.Left;
            _userSettings.TextOverlayTop    = _overlay.Top;
            _userSettings.TextOverlayWidth  = _overlay.Width;
            UserSettingsService.Save(_userSettings);
        }
        else
        {
            _voiceOverlay.Left  = _snapVoiceLeft;
            _voiceOverlay.Top   = _snapVoiceTop;
            _voiceOverlay.Width = _snapVoiceWidth;
            _overlay.Left   = _snapTextLeft;
            _overlay.Top    = _snapTextTop;
            _overlay.Width  = _snapTextWidth;
            _userSettings.OverlayOpacity = _snapOpacity;
            _voiceOverlay.SetOpacity(_snapOpacity);
            _overlay.SetOpacity(_snapOpacity);
            _langOverlay.SetOpacity(_snapOpacity);
        }

        _settings.Show();
        _settings.Activate();
    }
}
