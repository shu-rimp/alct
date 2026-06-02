using System.Windows;

namespace AlctClient.Views.Modals;

public partial class ApiConfigModal : Window
{
    public string ApiKey => ApiKeyBox.Password;

    public ApiConfigModal(string serviceName, string currentKey)
    {
        InitializeComponent();
        DialogTitleText.Text = $"{serviceName} API 설정";
        ApiKeyBox.Password = currentKey;
        ApiKeyPlaceholder.Visibility = string.IsNullOrEmpty(currentKey)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        ApiKeyPlaceholder.Visibility = string.IsNullOrEmpty(ApiKeyBox.Password)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Clipboard.ContainsText())
            ApiKeyBox.Password = System.Windows.Clipboard.GetText().Trim();
    }

    private void OnClear(object sender, RoutedEventArgs e) => ApiKeyBox.Password = string.Empty;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
