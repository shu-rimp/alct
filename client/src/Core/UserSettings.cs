namespace AlctClient.Core;

public class UserSettings
{
    public string SourceLang { get; set; } = "ja-JP";
    public bool CaptionModeEnabled { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.7;
    public bool ShowLanguageOverlay { get; set; } = false;

    public double VoiceOverlayLeft  { get; set; } = -1;
    public double VoiceOverlayTop   { get; set; } = 30;
    public double VoiceOverlayWidth { get; set; } = 500;

    public double TextOverlayLeft   { get; set; } = -1;
    public double TextOverlayTop    { get; set; } = 80;
    public double TextOverlayWidth  { get; set; } = 280;
}
