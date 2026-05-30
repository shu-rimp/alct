using System.Windows;

namespace AlctClient.Overlay;

public partial class SettingsWindow : Window
{
    public event Action<string>? SourceLangChanged;

    public string SourceLang => RadioJA.IsChecked == true ? "JA"
                              : RadioZH.IsChecked == true ? "ZH"
                              : "EN";

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnLanguageChanged(object sender, RoutedEventArgs e)
    {
        SourceLangChanged?.Invoke(SourceLang);
    }
}
