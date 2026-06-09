using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace AlctClient.Views.Modals;

file record GuideStep(string Number, string Text);

public partial class ApiConfigModal : Window
{
    public string DeepLApiKey  { get; private set; }
    public string GeminiApiKey { get; private set; }

    private string _currentEngine = "DeepL";

    private static readonly string DeepLGuideUrl  = "https://www.deepl.com/ko/your-account/keys";
    private static readonly string GeminiGuideUrl = "https://aistudio.google.com/app/apikey";

    public ApiConfigModal(string deepLKey, string geminiKey)
    {
        InitializeComponent();
        DeepLApiKey  = deepLKey;
        GeminiApiKey = geminiKey;
        Loaded += (_, _) => SelectEngine("DeepL");
    }

    private void SelectEngine(string engine)
    {
        _currentEngine = engine;

        SidebarDeepL.Style  = (Style)Resources[engine == "DeepL"  ? "SidebarItemActive" : "SidebarItem"];
        SidebarGemini.Style = (Style)Resources[engine == "Gemini" ? "SidebarItemActive" : "SidebarItem"];

        if (engine == "DeepL")
        {
            KeyLabel.Text      = "DeepL API 키";
            ApiKeyBox.Password = DeepLApiKey;
            GuideTitle.Text    = "📖 무료 키 발급 방법";
            GuideLinkBtn.Content = "🔗 DeepL 키 발급 페이지 열기";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "무료 플랜으로 가입합니다"),
                new GuideStep("2", "계정 설정 → API 키 메뉴로 이동"),
                new GuideStep("3", "키를 복사해서 위에 붙여넣습니다"),
            };
            WarnText.Text = "⚠️ 무료 플랜도 카드 등록을 요구하지만, 청구되지 않습니다.";
            WarnBox.Visibility = Visibility.Visible;
        }
        else
        {
            KeyLabel.Text      = "Gemini API 키";
            ApiKeyBox.Password = GeminiApiKey;
            GuideTitle.Text    = "📖 무료 키 발급 방법";
            GuideLinkBtn.Content = "🔗 Google AI Studio 열기";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "Google 계정으로 AI Studio에 로그인합니다"),
                new GuideStep("2", "Get API key → Create API key"),
                new GuideStep("3", "키를 복사해서 위에 붙여넣습니다"),
            };
            WarnBox.Visibility = Visibility.Collapsed;
        }

        UpdateKeyUi();
        UpdateDots();
    }

    private void UpdateKeyUi()
    {
        var hasKey = !string.IsNullOrEmpty(ApiKeyBox.Password);
        ApiKeyPlaceholder.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
        KeyValidLabel.Visibility     = hasKey ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void UpdateDots()
    {
        DotDeepL.Visibility  = string.IsNullOrEmpty(DeepLApiKey)  ? Visibility.Collapsed : Visibility.Visible;
        DotGemini.Visibility = string.IsNullOrEmpty(GeminiApiKey) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSelectDeepL(object sender, RoutedEventArgs e)  => SelectEngine("DeepL");
    private void OnSelectGemini(object sender, RoutedEventArgs e) => SelectEngine("Gemini");

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e) => UpdateKeyUi();

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Clipboard.ContainsText())
            ApiKeyBox.Password = System.Windows.Clipboard.GetText().Trim();
    }

    private void OnClear(object sender, RoutedEventArgs e) => ApiKeyBox.Password = string.Empty;

    private void OnOpenGuideLink(object sender, RoutedEventArgs e)
    {
        var url = _currentEngine == "DeepL" ? DeepLGuideUrl : GeminiGuideUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SaveCurrent();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveCurrent()
    {
        if (_currentEngine == "DeepL")
            DeepLApiKey = ApiKeyBox.Password;
        else
            GeminiApiKey = ApiKeyBox.Password;
    }
}
