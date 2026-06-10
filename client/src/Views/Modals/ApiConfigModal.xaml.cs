using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace AlctClient.Views.Modals;

file record GuideStep(string Number, string Text);

public partial class ApiConfigModal : Window
{
    private enum KeyState { None, Testing, Valid, Invalid, NetworkError }

    private static readonly HttpClient _validationHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string DeepLApiKey   { get; private set; }
    public string GeminiApiKey  { get; private set; }
    public string LangblyApiKey { get; private set; }

    private string _currentEngine = "";  // 빈 문자열 → 첫 SelectEngine 호출 시 저장 건너뜀
    private bool _suppressReset;
    private KeyState _deeplState   = KeyState.None;
    private KeyState _geminiState  = KeyState.None;
    private KeyState _langblyState = KeyState.None;

    private static readonly string DeepLGuideUrl   = "https://www.deepl.com/ko/signup?cta=checkout&is_api=true";
    private static readonly string DeepLUsageUrl   = "https://www.deepl.com/ko/pro-api?cta=v-api-member-portal";
    private static readonly string GeminiGuideUrl  = "https://aistudio.google.com/app/apikey";
    private static readonly string GeminiUsageUrl  = "https://aistudio.google.com/rate-limit";
    private static readonly string LangblyGuideUrl = "https://langbly.com/signup";
    private static readonly string LangblyUsageUrl = "https://langbly.com/dashboard";

    public ApiConfigModal(string deepLKey, string geminiKey, string langblyKey)
    {
        InitializeComponent();
        DeepLApiKey   = deepLKey;
        GeminiApiKey  = geminiKey;
        LangblyApiKey = langblyKey;
        Loaded += (_, _) => SelectEngine("DeepL");
    }

    private void SelectEngine(string engine)
    {
        if (_currentEngine == "DeepL")        DeepLApiKey   = ApiKeyBox.Password;
        else if (_currentEngine == "Gemini")  GeminiApiKey  = ApiKeyBox.Password;
        else if (_currentEngine == "Langbly") LangblyApiKey = ApiKeyBox.Password;

        _currentEngine = engine;

        SidebarDeepL.Style  = (Style)Resources[engine == "DeepL"  ? "SidebarItemActive" : "SidebarItem"];
        SidebarGemini.Style = (Style)Resources[engine == "Gemini" ? "SidebarItemActive" : "SidebarItem"];

        _suppressReset = true;
        if (engine == "DeepL")
        {
            KeyLabel.Text        = "DeepL API 키";
            ApiKeyBox.Password   = DeepLApiKey;
            GuideTitle.Text      = "무료 키 발급 방법";
            GuideLinkBtn.Content = "🔗 DeepL 키 발급 페이지 열기";
            UsageLinkBtn.Content = "🔗 API 사용량 확인";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "무료 플랜(Developer)으로 가입해 주세요."),
                new GuideStep("2", "구독에 필요한 플랜 정보를 입력해 주세요."),
                new GuideStep("3", "[계정] - [API 키 & 한도] - [키 생성 +] 으로 API 키를 발급받아 주세요."),
                new GuideStep("4", "키를 복사한 후, '붙여넣기' 버튼을 클릭해 주세요."),
            };
            WarnText.Text = "⚠️ 무료 플랜도 카드 등록을 요구하지만, 청구되지 않아요.";
            WarnBox.Visibility = Visibility.Visible;
        }
        else if (engine == "Gemini")
        {
            KeyLabel.Text        = "Gemini API 키";
            ApiKeyBox.Password   = GeminiApiKey;
            GuideTitle.Text      = "무료 키 발급 방법";
            GuideLinkBtn.Content = "🔗 Google AI Studio 열기";
            UsageLinkBtn.Content = "🔗 API 사용량 확인";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "Google 계정으로 AI Studio에 로그인해 주세요."),
                new GuideStep("2", "화면 좌측 하단 4번째 키모양 버튼(Get API key)을 클릭해 주세요."),
                new GuideStep("3", "표시된 목록에서 우측 1번째 복사모양 버튼(API 키 복사)을 클릭해 주세요."),
                new GuideStep("4", "키를 복사한 후, '붙여넣기' 버튼을 클릭해 주세요."),
            };
            WarnBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            KeyLabel.Text        = "Langbly API 키";
            ApiKeyBox.Password   = LangblyApiKey;
            GuideTitle.Text      = "키 발급 방법";
            GuideLinkBtn.Content = "🔗 Langbly 가입 페이지 열기";
            UsageLinkBtn.Content = "🔗 대시보드 열기";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "Langbly에 가입하고 카드를 등록해 주세요."),
                new GuideStep("2", "대시보드에서 API 키를 발급받아 주세요."),
                new GuideStep("3", "지출 한도를 설정해 예상치 못한 청구를 방지하세요."),
                new GuideStep("4", "키를 복사한 후, '붙여넣기' 버튼을 클릭해 주세요."),
            };
            WarnText.Text = "⚠️ 사용량에 따라 요금이 청구돼요. 대시보드에서 지출 한도를 설정할 수 있어요.";
            WarnBox.Visibility = Visibility.Visible;
        }
        _suppressReset = false;

        UpdateKeyUi();
        UpdateDots();
        ApplyKeyState(engine switch
        {
            "DeepL"  => _deeplState,
            "Gemini" => _geminiState,
            _        => _langblyState,
        });
    }

    private void UpdateKeyUi()
    {
        var hasKey = !string.IsNullOrEmpty(ApiKeyBox.Password);
        ApiKeyPlaceholder.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyKeyState(KeyState state)
    {
        if (_currentEngine == "DeepL")        _deeplState   = state;
        else if (_currentEngine == "Gemini")  _geminiState  = state;
        else                                  _langblyState = state;

        switch (state)
        {
            case KeyState.None:
                KeyValidLabel.Visibility = Visibility.Collapsed;
                break;
            case KeyState.Testing:
                KeyValidLabel.Visibility = Visibility.Visible;
                KeyValidLabel.Text       = "🔍 검증 중...";
                KeyValidLabel.Foreground = (WpfBrushBase)FindResource("TextSecondaryBrush");
                break;
            case KeyState.Valid:
                KeyValidLabel.Visibility = Visibility.Visible;
                KeyValidLabel.Text       = "✓ 유효한 키예요";
                KeyValidLabel.Foreground = new WpfBrush(WpfColor.FromRgb(0x4a, 0xde, 0x80));
                break;
            case KeyState.Invalid:
                KeyValidLabel.Visibility = Visibility.Visible;
                KeyValidLabel.Text       = "✗ 유효하지 않은 키예요";
                KeyValidLabel.Foreground = (WpfBrushBase)FindResource("TextWarnBrush");
                break;
            case KeyState.NetworkError:
                KeyValidLabel.Visibility = Visibility.Visible;
                KeyValidLabel.Text       = "⚠ 일시적 오류예요. 잠시 후 다시 시도해주세요";
                KeyValidLabel.Foreground = (WpfBrushBase)FindResource("TextWarnBrush");
                break;
        }

        SaveBtn.IsEnabled = state is not (KeyState.Invalid or KeyState.NetworkError);
    }

    private void UpdateDots()
    {
        DotDeepL.Visibility  = string.IsNullOrEmpty(DeepLApiKey)  ? Visibility.Collapsed : Visibility.Visible;
        DotGemini.Visibility = string.IsNullOrEmpty(GeminiApiKey) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSelectDeepL(object sender, RoutedEventArgs e)   => SelectEngine("DeepL");
    private void OnSelectGemini(object sender, RoutedEventArgs e)  => SelectEngine("Gemini");
    private void OnSelectLangbly(object sender, RoutedEventArgs e) => SelectEngine("Langbly");

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateKeyUi();
        if (!_suppressReset)
            ApplyKeyState(KeyState.None);
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        if (!System.Windows.Clipboard.ContainsText()) return;
        var key = System.Windows.Clipboard.GetText().Trim();
        if (string.IsNullOrEmpty(key)) return;

        _suppressReset = true;
        ApiKeyBox.Password = key;
        _suppressReset = false;
        UpdateKeyUi();

        if (!IsAscii(key)) { ApplyKeyState(KeyState.Invalid); return; }

        ApplyKeyState(KeyState.Testing);
        try
        {
            var valid = _currentEngine switch
            {
                "DeepL"  => await ValidateDeepLAsync(key),
                "Gemini" => await ValidateGeminiAsync(key),
                _        => await ValidateLangblyAsync(key),
            };

            ApplyKeyState(valid ? KeyState.Valid : KeyState.Invalid);
            if (valid && _currentEngine == "Langbly")
                _ = ApplyLangblySpendingCapAsync(key);
        }
        catch
        {
            ApplyKeyState(KeyState.NetworkError);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e) => ApiKeyBox.Password = string.Empty;

    private static bool IsAscii(string s) => s.All(c => c < 128);

    private static async Task<bool> ValidateDeepLAsync(string apiKey)
    {
        var baseUrl = apiKey.EndsWith(":fx")
            ? "https://api-free.deepl.com/v2/usage"
            : "https://api.deepl.com/v2/usage";

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {apiKey}");
        var response = await _validationHttp.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static async Task<bool> ValidateGeminiAsync(string apiKey)
    {
        const string model = "gemini-2.5-flash-lite";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text = "hi" } } } },
            generationConfig = new { maxOutputTokens = 1 },
        };
        var response = await _validationHttp.PostAsync(url, JsonContent.Create(payload));
        return response.IsSuccessStatusCode;
    }

    private void OnOpenGuideLink(object sender, RoutedEventArgs e)
    {
        var url = _currentEngine switch
        {
            "DeepL"  => DeepLGuideUrl,
            "Gemini" => GeminiGuideUrl,
            _        => LangblyGuideUrl,
        };
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnOpenUsageLink(object sender, RoutedEventArgs e)
    {
        var url = _currentEngine switch
        {
            "DeepL"  => DeepLUsageUrl,
            "Gemini" => GeminiUsageUrl,
            _        => LangblyUsageUrl,
        };
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

    private static async Task<bool> ValidateLangblyAsync(string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.langbly.com/v2/account/spending-limit");
        request.Headers.Add("X-API-Key", apiKey);
        var response = await _validationHttp.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static async Task ApplyLangblySpendingCapAsync(string apiKey)
    {
        try
        {
            var payload = new { limitDollars = 0, limitCents = 0 };
            using var request = new HttpRequestMessage(HttpMethod.Put,
                "https://api.langbly.com/v2/account/spending-limit")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Add("X-API-Key", apiKey);
            await _validationHttp.SendAsync(request);
        }
        catch { }
    }

    private void SaveCurrent()
    {
        if (_currentEngine == "DeepL")        DeepLApiKey   = ApiKeyBox.Password;
        else if (_currentEngine == "Gemini")  GeminiApiKey  = ApiKeyBox.Password;
        else                                  LangblyApiKey = ApiKeyBox.Password;
    }
}
