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
    private readonly DragSelectOverlay _dragSelectOverlay = new();

    private double _snapVoiceLeft, _snapVoiceTop, _snapVoiceWidth;
    private double _snapOpacity;
    private double _snapFontSize;

    private void InitOverlays()
    {
        var screens = GetSortedScreens();
        if (_userSettings.MonitorIndex > 0 && _userSettings.MonitorIndex < screens.Length)
            _langOverlay.SetInitialScreen(screens[_userSettings.MonitorIndex]);

        _langOverlay.SetLanguage(_userSettings.SourceLang);
        _langOverlay.SetCaptionMode(_userSettings.CaptionModeEnabled);
        _langOverlay.SetOpacity(_userSettings.OverlayOpacity);
        if (_userSettings.ShowLanguageOverlay && _userSettings.OnboardingComplete) _langOverlay.Show();

        LoadOverlayPositions();
        // 채팅 오버레이(_overlay)는 고정 50% 반투명 배경 — 공유 투명도 설정과 무관(순수 검정이라 더 진하게 보임)
        _voiceOverlay.SetOpacity(_userSettings.OverlayOpacity);
        _langOverlay.SetOpacity(_userSettings.OverlayOpacity);
        _overlay.SetFontSize(_userSettings.OverlayFontSize);
        _voiceOverlay.SetFontSize(_userSettings.OverlayFontSize);
        _overlay.SetAutoHideSeconds(_userSettings.ChatHideSeconds);

        // ESC 숨김 단축키는 채팅 오버레이가 보일 때만 등록 — 평소 게임의 ESC를 막지 않는다.
        _overlay.IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _hotkeyManager?.RegisterDismiss();
            else _hotkeyManager?.UnregisterDismiss();
        };

        _editPanel.OpacityChanged += opacity =>
        {
            _voiceOverlay.SetOpacity(opacity);
            _langOverlay.SetOpacity(opacity);
            _userSettings.OverlayOpacity = opacity;
        };
        _editPanel.FontSizeChanged += size =>
        {
            _overlay.SetFontSize(size);
            _voiceOverlay.SetFontSize(size);
            _userSettings.OverlayFontSize = size;
        };
        _editPanel.SaveRequested   += () => ExitEditMode(save: true);
        _editPanel.CancelRequested += () => ExitEditMode(save: false);
        _editPanel.ResetRequested  += ResetOverlaysToDefault;

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

        // 길게 누르기 → 드래그 영역 선택 → 그 영역으로 1회 캡처(저장 영역은 그대로)
        _dragSelectOverlay.SelectionCompleted += region =>
            // 딤 오버레이가 화면에서 사라진 뒤 캡처되도록 한 프레임 양보 + 약간의 지연
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(120);
                RunCapture(region);
            });
    }

    // ── Monitor switching ──

    internal void TranslateAllOverlaysToMonitor(
        System.Windows.Forms.Screen from, System.Windows.Forms.Screen to)
    {
        // 채팅 오버레이(_overlay)는 캡처 영역 위에 매번 자동 배치되므로 저장 위치 이동 대상이 아니다.
        TranslateOverlayToMonitor(_voiceOverlay, from, to, _userSettings.VoiceOverlayLeft, _userSettings.VoiceOverlayTop);
        TranslateOverlayToMonitor(_langOverlay,  from, to, _langOverlay.Left,              _langOverlay.Top);
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
        Window overlay, System.Windows.Forms.Screen from, System.Windows.Forms.Screen to,
        double currentLeft, double currentTop)
    {
        if (double.IsNaN(currentLeft) || double.IsNaN(currentTop)) return;
        double relX = (currentLeft - from.Bounds.X) / from.Bounds.Width;
        double relY = (currentTop  - from.Bounds.Y) / from.Bounds.Height;
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

    // 채팅 오버레이(_overlay)는 위치를 저장하지 않는다 — 캡처 영역 위에 매번 자동 배치된다.
    private void SaveOverlayPositions()
    {
        _userSettings.VoiceOverlayLeft  = _voiceOverlay.Left;
        _userSettings.VoiceOverlayTop   = _voiceOverlay.Top;
        _userSettings.VoiceOverlayWidth = _voiceOverlay.Width;
    }

    private void LoadOverlayPositions()
    {
        var screen = GetSelectedScreen();
        _voiceOverlay.LoadBounds(_userSettings.VoiceOverlayLeft, _userSettings.VoiceOverlayTop, _userSettings.VoiceOverlayWidth);

        if (_userSettings.VoiceOverlayLeft < 0 || !screen.Bounds.Contains(
                (int)_userSettings.VoiceOverlayLeft, (int)_userSettings.VoiceOverlayTop))
            _voiceOverlay.MoveToMonitor(screen);

        SaveOverlayPositions();
    }

    // ── Screen ──

    private static System.Windows.Forms.Screen[] GetSortedScreens() =>
        System.Windows.Forms.Screen.AllScreens
            .OrderBy(s => s.Bounds.Left)
            .ThenBy(s => s.Bounds.Top)
            .ToArray();

    private System.Windows.Forms.Screen GetSelectedScreen()
    {
        var screens = GetSortedScreens();
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
        _snapOpacity    = _userSettings.OverlayOpacity;
        _snapFontSize   = _userSettings.OverlayFontSize;

        // 채팅 오버레이(_overlay)는 캡처 영역 위에 자동 배치되므로 편집 모드에 참여하지 않는다.
        // 투명도/글자 크기 조절은 음성 오버레이 미리보기로 확인하며, 같은 값이 채팅에도 적용된다.
        _voiceOverlay.SetEditMode(true);
        _editPanel.SetOpacity(_userSettings.OverlayOpacity);
        _editPanel.SetFontSize(_userSettings.OverlayFontSize);
        _editPanel.Show();
        _editPanel.MoveToMonitor(GetSelectedScreen());
        _settings.Hide();
    }

    private void ResetOverlaysToDefault()
    {
        var defaults = new UserSettings();
        var screen = GetSelectedScreen();

        _voiceOverlay.ResetBounds(screen);
        _langOverlay.MoveToMonitor(screen);

        _voiceOverlay.SetOpacity(defaults.OverlayOpacity);
        _langOverlay.SetOpacity(defaults.OverlayOpacity);
        _overlay.SetFontSize(defaults.OverlayFontSize);
        _voiceOverlay.SetFontSize(defaults.OverlayFontSize);

        _userSettings.OverlayOpacity  = defaults.OverlayOpacity;
        _userSettings.OverlayFontSize = defaults.OverlayFontSize;

        _editPanel.SetOpacity(defaults.OverlayOpacity);
        _editPanel.SetFontSize(defaults.OverlayFontSize);
    }

    private void ExitEditMode(bool save)
    {
        _voiceOverlay.SetEditMode(false);
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
            _userSettings.OverlayOpacity  = _snapOpacity;
            _userSettings.OverlayFontSize = _snapFontSize;
            _voiceOverlay.SetOpacity(_snapOpacity);
            _langOverlay.SetOpacity(_snapOpacity);
            _voiceOverlay.SetFontSize(_snapFontSize);
            _overlay.SetFontSize(_snapFontSize);
        }

        _settings.Show();
        _settings.Activate();
    }
}
