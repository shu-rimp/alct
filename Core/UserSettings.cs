namespace AlctClient.Core;

public class UserSettings
{
    public string SourceLang { get; set; } = "ja-JP";
    public bool CaptionModeEnabled { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.7;
    public bool ShowLanguageOverlay { get; set; } = false;
}
