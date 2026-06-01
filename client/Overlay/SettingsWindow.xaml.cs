using System.Windows;

namespace AlctClient.Overlay;

public partial class SettingsWindow : Window
{
    public event Action<string>? SourceLangChanged;
    public event Action<bool>? CaptionModeChanged;
    public event Action<string>? DeepLApiKeyChanged;

    public string SourceLang => RadioJA.IsChecked == true ? "JA"
                              : RadioZH.IsChecked == true ? "ZH"
                              : "EN";

    public string DeepLApiKey => DeepLApiKeyBox.Text;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public void SetDeepLApiKey(string key)
    {
        DeepLApiKeyBox.Text = key;
    }

    private void OnLanguageChanged(object sender, RoutedEventArgs e)
    {
        SourceLangChanged?.Invoke(SourceLang);
    }

    private void OnCaptionModeChanged(object sender, RoutedEventArgs e)
    {
        CaptionModeChanged?.Invoke(CaptionMonitorToggle.IsChecked == true);
    }

    private void OnDeepLKeyChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        DeepLApiKeyChanged?.Invoke(DeepLApiKeyBox.Text);
    }
}
