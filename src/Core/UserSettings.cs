namespace AlctClient.Core;

public class UserSettings
{
    public string SourceLang { get; set; } = "ja-JP";
    public bool CaptionModeEnabled { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayFontSize { get; set; } = 14;
    public bool ShowLanguageOverlay { get; set; } = true;
    public int ChatHideSeconds { get; set; } = 5;  // 채팅 번역 오버레이 자동 숨김(초). 0 = 무제한(ESC/다음 캡처로만 숨김)

    public double VoiceOverlayLeft  { get; set; } = -1;
    public double VoiceOverlayTop   { get; set; } = 30;
    public double VoiceOverlayWidth { get; set; } = 700;

    public double TextOverlayLeft   { get; set; } = -1;
    public double TextOverlayTop    { get; set; } = 80;
    public double TextOverlayWidth  { get; set; } = 280;

    public int MonitorIndex { get; set; } = 0;

    public bool UseCustomCaptureRegion { get; set; } = false;
    public int CustomCaptureX      { get; set; } = 0;
    public int CustomCaptureY      { get; set; } = 0;
    public int CustomCaptureWidth  { get; set; } = 0;
    public int CustomCaptureHeight { get; set; } = 0;

    public uint CaptureHotkeyModifiers { get; set; } = (uint)HotkeyModifiers.Ctrl;
    public uint CaptureHotkeyVKey      { get; set; } = 0x54; // T
    public uint InputHotkeyModifiers   { get; set; } = (uint)HotkeyModifiers.Ctrl;
    public uint InputHotkeyVKey        { get; set; } = 0x47; // G

    public bool OnboardingComplete { get; set; } = false;
}
