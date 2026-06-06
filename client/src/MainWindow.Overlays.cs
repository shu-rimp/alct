using AlctClient.Core;
using AlctClient.Utils;
using AlctClient.Views.Overlays;
using System.Drawing;
using System.Windows;

namespace AlctClient;

public partial class MainWindow
{
    private readonly EditPanelOverlay _editPanel = new();
    private readonly CaptureRegionOverlay _captureRegionOverlay = new();

    private double _snapVoiceLeft, _snapVoiceTop, _snapVoiceWidth;
    private double _snapTextLeft,  _snapTextTop,  _snapTextWidth;
    private double _snapOpacity;

    private void InitOverlays()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_userSettings.MonitorIndex > 0 && _userSettings.MonitorIndex < screens.Length)
            _langOverlay.SetInitialScreen(screens[_userSettings.MonitorIndex]);

        _langOverlay.SetLanguage(_userSettings.SourceLang);
        _langOverlay.SetCaptionMode(_userSettings.CaptionModeEnabled);
        _langOverlay.SetOpacity(_userSettings.OverlayOpacity);
        if (_userSettings.ShowLanguageOverlay && _userSettings.OnboardingComplete) _langOverlay.Show();

        LoadOverlayPositions();
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

        _captureRegionOverlay.SaveRequested += region =>
        {
            _userSettings.UseCustomCaptureRegion = true;
            SaveCustomCaptureRegion(region);
            UserSettingsService.Save(_userSettings);
            _settings.Show();
            _settings.Activate();
        };
        _captureRegionOverlay.CancelRequested += () =>
        {
            _settings.Show();
            _settings.Activate();
        };
    }

    // ── Monitor switching ──

    internal void TranslateAllOverlaysToMonitor(
        System.Windows.Forms.Screen from, System.Windows.Forms.Screen to)
    {
        TranslateOverlayToMonitor(_overlay,      from, to);
        TranslateOverlayToMonitor(_voiceOverlay, from, to);
        TranslateOverlayToMonitor(_langOverlay,  from, to);
        _langOverlay.SetInitialScreen(to);
    }

    internal void UpdateCaptureRegionForMonitor(
        System.Windows.Forms.Screen from, System.Windows.Forms.Screen to)
    {
        if (_userSettings.UseCustomCaptureRegion && _userSettings.CustomCaptureWidth > 0)
            SaveCustomCaptureRegion(TranslateCaptureRegion(GetCustomCaptureRegion(), from, to));
        else
            _screenCapture.SetCaptureRegion(ScreenCaptureService.GetDefaultCaptureRegion(to));
    }

    private static void TranslateOverlayToMonitor(
        Window overlay, System.Windows.Forms.Screen from, System.Windows.Forms.Screen to)
    {
        if (double.IsNaN(overlay.Left) || double.IsNaN(overlay.Top)) return;
        double relX = (overlay.Left - from.Bounds.X) / from.Bounds.Width;
        double relY = (overlay.Top  - from.Bounds.Y) / from.Bounds.Height;
        overlay.Left = to.Bounds.X + Math.Clamp(relX, 0, 1) * to.Bounds.Width;
        overlay.Top  = to.Bounds.Y + Math.Clamp(relY, 0, 1) * to.Bounds.Height;
    }

    private static Rectangle TranslateCaptureRegion(
        Rectangle region, System.Windows.Forms.Screen from, System.Windows.Forms.Screen to)
    {
        double relX = (double)(region.X - from.Bounds.X) / from.Bounds.Width;
        double relY = (double)(region.Y - from.Bounds.Y) / from.Bounds.Height;
        double relW = (double)region.Width  / from.Bounds.Width;
        double relH = (double)region.Height / from.Bounds.Height;
        return new Rectangle(
            to.Bounds.X + (int)Math.Round(Math.Clamp(relX, 0, 1) * to.Bounds.Width),
            to.Bounds.Y + (int)Math.Round(Math.Clamp(relY, 0, 1) * to.Bounds.Height),
            (int)Math.Round(relW * to.Bounds.Width),
            (int)Math.Round(relH * to.Bounds.Height));
    }

    // ── Capture region ──

    private Rectangle GetCustomCaptureRegion() => new(
        _userSettings.CustomCaptureX, _userSettings.CustomCaptureY,
        _userSettings.CustomCaptureWidth, _userSettings.CustomCaptureHeight);

    private void SaveCustomCaptureRegion(Rectangle region)
    {
        _userSettings.CustomCaptureX      = region.X;
        _userSettings.CustomCaptureY      = region.Y;
        _userSettings.CustomCaptureWidth  = region.Width;
        _userSettings.CustomCaptureHeight = region.Height;
        _screenCapture.SetCaptureRegion(region);
    }

    // ── Overlay position persistence ──

    private void SaveOverlayPositions()
    {
        _userSettings.VoiceOverlayLeft  = _voiceOverlay.Left;
        _userSettings.VoiceOverlayTop   = _voiceOverlay.Top;
        _userSettings.VoiceOverlayWidth = _voiceOverlay.Width;
        _userSettings.TextOverlayLeft   = _overlay.Left;
        _userSettings.TextOverlayTop    = _overlay.Top;
        _userSettings.TextOverlayWidth  = _overlay.Width;
    }

    private void LoadOverlayPositions()
    {
        var screen = GetSelectedScreen();
        _voiceOverlay.LoadBounds(_userSettings.VoiceOverlayLeft, _userSettings.VoiceOverlayTop, _userSettings.VoiceOverlayWidth);
        _overlay.LoadBounds(_userSettings.TextOverlayLeft, _userSettings.TextOverlayTop, _userSettings.TextOverlayWidth);

        if (_userSettings.VoiceOverlayLeft < 0 || !screen.Bounds.Contains(
                (int)_userSettings.VoiceOverlayLeft, (int)_userSettings.VoiceOverlayTop))
            _voiceOverlay.MoveToMonitor(screen);
        if (_userSettings.TextOverlayLeft < 0 || !screen.Bounds.Contains(
                (int)_userSettings.TextOverlayLeft, (int)_userSettings.TextOverlayTop))
            _overlay.MoveToMonitor(screen);
    }

    // ── Screen ──

    private System.Windows.Forms.Screen GetSelectedScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        return _userSettings.MonitorIndex < screens.Length
            ? screens[_userSettings.MonitorIndex]
            : System.Windows.Forms.Screen.PrimaryScreen!;
    }

    // ── Edit mode ──

    private void EnterCaptureRegionEditMode()
    {
        var current = _userSettings.UseCustomCaptureRegion && _userSettings.CustomCaptureWidth > 0
            ? GetCustomCaptureRegion()
            : ScreenCaptureService.GetDefaultCaptureRegion(GetSelectedScreen());

        _captureRegionOverlay.LoadRegion(current, GetSelectedScreen());
        _captureRegionOverlay.Show();
        _settings.Hide();
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
        _editPanel.MoveToMonitor(GetSelectedScreen());
        _settings.Hide();
    }

    private void ExitEditMode(bool save)
    {
        _voiceOverlay.SetEditMode(false);
        _overlay.SetEditMode(false);
        _editPanel.Hide();

        if (save)
        {
            SaveOverlayPositions();
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
