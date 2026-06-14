using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
    public string MyMemoryEmail { get; private set; }

    private string _currentEngine = "";  // 빈 문자열 → 첫 SelectEngine 호출 시 저장 건너뜀
    private bool _suppressReset;
    private KeyState _deeplState   = KeyState.None;
    private KeyState _geminiState  = KeyState.None;
    private KeyState _langblyState = KeyState.None;
    private (long count, long limit)? _deeplUsage;
    private bool _deeplUsageFetched;  // 모달 열려있는 동안 탭 전환마다 재조회하지 않도록 캐시

    private static readonly string DeepLGuideUrl   = "https://www.deepl.com/ko/signup?cta=checkout&is_api=true";
    private static readonly string GeminiGuideUrl  = "https://aistudio.google.com/app/apikey";
    private static readonly string GeminiUsageUrl  = "https://aistudio.google.com/rate-limit";
    private static readonly string LangblyGuideUrl = "https://langbly.com/signup";
    private static readonly string LangblyUsageUrl = "https://langbly.com/dashboard";

    public ApiConfigModal(string deepLKey, string geminiKey, string langblyKey, string myMemoryEmail)
    {
        InitializeComponent();
        DeepLApiKey   = deepLKey;
        GeminiApiKey  = geminiKey;
        LangblyApiKey = langblyKey;
        MyMemoryEmail = myMemoryEmail;
        _deeplState   = string.IsNullOrEmpty(deepLKey)   ? KeyState.None : KeyState.Valid;
        _geminiState  = string.IsNullOrEmpty(geminiKey)  ? KeyState.None : KeyState.Valid;
        _langblyState = string.IsNullOrEmpty(langblyKey) ? KeyState.None : KeyState.Valid;
        Loaded += (_, _) => SelectEngine("DeepL");
    }

    private void SelectEngine(string engine)
    {
        if (_currentEngine == "MyMemory")     MyMemoryEmail = EmailBox.Text.Trim();
        else if (_currentEngine == "DeepL")   DeepLApiKey   = ApiKeyBox.Password;
        else if (_currentEngine == "Gemini")  GeminiApiKey  = ApiKeyBox.Password;
        else if (_currentEngine == "Langbly") LangblyApiKey = ApiKeyBox.Password;

        _currentEngine = engine;

        SidebarMyMemory.Style = (Style)Resources[engine == "MyMemory" ? "SidebarItemActive" : "SidebarItem"];
        SidebarDeepL.Style    = (Style)Resources[engine == "DeepL"    ? "SidebarItemActive" : "SidebarItem"];
        SidebarGemini.Style   = (Style)Resources[engine == "Gemini"   ? "SidebarItemActive" : "SidebarItem"];

        _suppressReset = true;

        // 기본: 키(PasswordBox) 입력 레이아웃 — MyMemory 분기에서 이메일 레이아웃으로 덮어씀
        ApiKeyBox.Visibility     = Visibility.Visible;
        EmailBox.Visibility      = Visibility.Collapsed;
        KeyShareNotice.Visibility = Visibility.Visible;
        GuideLinkBtn.Visibility  = Visibility.Visible;
        ApiKeyPlaceholder.Text   = "붙여넣기 버튼 클릭";

        if (engine == "MyMemory")
        {
            KeyLabel.Text            = "MyMemory 이메일 (선택)";
            EmailBox.Text            = MyMemoryEmail;
            ApiKeyBox.Visibility     = Visibility.Collapsed;
            EmailBox.Visibility      = Visibility.Visible;
            KeyShareNotice.Visibility = Visibility.Collapsed;
            KeyValidLabel.Visibility = Visibility.Collapsed;
            GuideLinkBtn.Visibility  = Visibility.Collapsed;
            UsageLinkBtn.Visibility  = Visibility.Collapsed;
            UsageHint.Visibility     = Visibility.Collapsed;
            ApiKeyPlaceholder.Text   = "you@example.com";
            GuideTitle.Text          = "이메일 등록 안내";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("•", "이메일을 입력하면 일일 번역 한도가 5,000자 → 50,000자로 늘어나요."),
                new GuideStep("•", "입력하지 않아도 기본 한도(5,000자)로 바로 사용할 수 있어요."),
            };
            WarnBox.Visibility = Visibility.Collapsed;
        }
        else if (engine == "DeepL")
        {
            KeyLabel.Text        = "DeepL API 키";
            ApiKeyBox.Password   = DeepLApiKey;
            GuideTitle.Text      = "무료 키 발급 방법";
            GuideLinkBtn.Content = "🔗 DeepL 키 발급하기";
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "Developer 플랜(무료)으로 가입해 주세요."),
                new GuideStep("2", "[계정] - [API 키 & 한도] - [키 생성 +] 으로 API 키를 발급받아 주세요."),
                new GuideStep("3", "키를 복사한 후, '붙여넣기' 버튼을 클릭해 주세요."),
            };
            WarnText.Text = "⚠️ 무료 플랜도 카드 등록을 요구하지만, 청구되지 않아요.";
            WarnBox.Visibility = Visibility.Visible;
            UsageLinkBtn.Visibility = Visibility.Collapsed;  // DeepL은 모달 내 사용량 게이지로 대체 
            UsageHint.Visibility = Visibility.Collapsed;
        }
        else if (engine == "Gemini")
        {
            KeyLabel.Text        = "Gemini API 키";
            ApiKeyBox.Password   = GeminiApiKey;
            GuideTitle.Text      = "무료 키 발급 방법";
            GuideLinkBtn.Content = "🔗 Gemini 키 발급하기";
            UsageLinkBtn.Content = "🔗 API 사용량 확인";
            UsageLinkBtn.Visibility = Visibility.Visible;
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "Google 계정으로 AI Studio에 로그인해 주세요."),
                new GuideStep("2", "화면 좌측 하단 4번째 키모양 버튼(Get API key)을 클릭해 주세요."),
                new GuideStep("3", "표시된 목록에서 우측 1번째 복사모양 버튼(API 키 복사)을 클릭해 주세요."),
                new GuideStep("4", "키를 복사한 후, '붙여넣기' 버튼을 클릭해 주세요."),
            };
            UsageHint.Guide = "기간을 1일로 설정하고, RPD값을 확인하면 돼요. 오늘 한도를 다 소진했어도, 내일 다시 사용할 수 있어요.";
            UsageHint.Visibility = Visibility.Visible;
            WarnBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            KeyLabel.Text        = "Langbly API 키";
            ApiKeyBox.Password   = LangblyApiKey;
            GuideTitle.Text      = "키 발급 방법";
            GuideLinkBtn.Content = "🔗 Langbly 가입 페이지 열기";
            UsageLinkBtn.Content = "🔗 대시보드 열기";
            UsageLinkBtn.Visibility = Visibility.Visible;
            GuideSteps.ItemsSource = new[]
            {
                new GuideStep("1", "Langbly에 가입하고 카드를 등록해 주세요."),
                new GuideStep("2", "대시보드에서 API 키를 발급받아 주세요."),
                new GuideStep("3", "지출 한도를 설정해 예상치 못한 청구를 방지하세요."),
                new GuideStep("4", "키를 복사한 후, '붙여넣기' 버튼을 클릭해 주세요."),
            };
            WarnText.Text = "⚠️ 사용량에 따라 요금이 청구돼요. 대시보드에서 지출 한도를 설정할 수 있어요.";
            WarnBox.Visibility = Visibility.Visible;
            UsageHint.Visibility = Visibility.Collapsed;
        }
        _suppressReset = false;

        UpdateKeyUi();
        UpdateDots();
        if (engine != "MyMemory")  // MyMemory는 이메일이라 키 검증 상태 표시 없음
            ApplyKeyState(engine switch
            {
                "DeepL"  => _deeplState,
                "Gemini" => _geminiState,
                _        => _langblyState,
            });

        if (engine == "DeepL") _ = RefreshDeepLUsageAsync(ApiKeyBox.Password);
        else UsageBox.Visibility = Visibility.Collapsed;
    }

    private void UpdateKeyUi()
    {
        var hasInput = _currentEngine == "MyMemory"
            ? !string.IsNullOrEmpty(EmailBox.Text)
            : !string.IsNullOrEmpty(ApiKeyBox.Password);
        ApiKeyPlaceholder.Visibility = hasInput ? Visibility.Collapsed : Visibility.Visible;
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

        SaveBtn.IsEnabled =
            _deeplState   is not (KeyState.Invalid or KeyState.NetworkError) &&
            _geminiState  is not (KeyState.Invalid or KeyState.NetworkError) &&
            _langblyState is not (KeyState.Invalid or KeyState.NetworkError);
    }

    private void UpdateDots()
    {
        DotDeepL.Visibility  = _deeplState  == KeyState.Valid ? Visibility.Visible : Visibility.Collapsed;
        DotGemini.Visibility = _geminiState == KeyState.Valid ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectMyMemory(object sender, RoutedEventArgs e) => SelectEngine("MyMemory");
    private void OnSelectDeepL(object sender, RoutedEventArgs e)   => SelectEngine("DeepL");
    private void OnSelectGemini(object sender, RoutedEventArgs e)  => SelectEngine("Gemini");
    private void OnSelectLangbly(object sender, RoutedEventArgs e) => SelectEngine("Langbly");

    // MyMemory 이메일은 검증 API가 없어 입력값만 갱신 (placeholder 토글)
    private void OnEmailChanged(object sender, TextChangedEventArgs e) => UpdateKeyUi();

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateKeyUi();
        if (_suppressReset) return;
        ApplyKeyState(KeyState.None);
        if (_currentEngine == "DeepL")
        {
            _deeplUsageFetched = false;
            UsageBox.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        if (!System.Windows.Clipboard.ContainsText()) return;
        var key = System.Windows.Clipboard.GetText().Trim();
        if (string.IsNullOrEmpty(key)) return;

        if (_currentEngine == "MyMemory")  // 이메일은 검증 없이 입력만
        {
            EmailBox.Text = key;
            return;
        }

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

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_currentEngine == "MyMemory") EmailBox.Text = string.Empty;
        else ApiKeyBox.Password = string.Empty;
    }

    private static bool IsAscii(string s) => s.All(c => c < 128);

    // 검증과 사용량 조회가 같은 /v2/usage 엔드포인트 — 한 번의 호출로 둘 다 처리
    private async Task<bool> ValidateDeepLAsync(string apiKey)
    {
        _deeplUsage = await FetchDeepLUsageAsync(apiKey);
        _deeplUsageFetched = _deeplUsage is not null;
        ShowDeepLUsage(_deeplUsage);
        return _deeplUsage is not null;
    }

    private async Task RefreshDeepLUsageAsync(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) { ShowDeepLUsage(null); return; }
        if (!_deeplUsageFetched)
        {
            try
            {
                _deeplUsage = await FetchDeepLUsageAsync(apiKey);
                _deeplUsageFetched = _deeplUsage is not null;
            }
            catch { _deeplUsage = null; }
        }
        ShowDeepLUsage(_deeplUsage);
    }

    private void ShowDeepLUsage((long count, long limit)? usage)
    {
        if (_currentEngine != "DeepL" || usage is not { limit: > 0 })
        {
            UsageBox.Visibility = Visibility.Collapsed;
            return;
        }
        var (count, limit) = usage.Value;
        var ratio = Math.Clamp((double)count / limit, 0, 1);
        UsageText.Text = $"{count:N0} / {limit:N0}자 ({ratio:P1})";
        UsageFillCol.Width = new GridLength(ratio, GridUnitType.Star);
        UsageRestCol.Width = new GridLength(1 - ratio, GridUnitType.Star);
        UsageFill.Background = new WpfBrush(ratio switch
        {
            >= 0.95 => WpfColor.FromRgb(0xef, 0x44, 0x44),
            >= 0.80 => WpfColor.FromRgb(0xF0, 0xA8, 0x68),
            _       => WpfColor.FromRgb(0x4a, 0xde, 0x80),
        });
        UsageBox.Visibility = Visibility.Visible;
    }

    private static async Task<(long count, long limit)?> FetchDeepLUsageAsync(string apiKey)
    {
        var baseUrl = apiKey.EndsWith(":fx")
            ? "https://api-free.deepl.com/v2/usage"
            : "https://api.deepl.com/v2/usage";

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {apiKey}");
        var response = await _validationHttp.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (doc.RootElement.GetProperty("character_count").GetInt64(),
                doc.RootElement.GetProperty("character_limit").GetInt64());
    }

    private static async Task<bool> ValidateGeminiAsync(string apiKey)
    {
        const string model = "gemini-3.1-flash-lite";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text = "hi" } } } },
            generationConfig = new { maxOutputTokens = 1 },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("x-goog-api-key", apiKey);  
        var response = await _validationHttp.SendAsync(request);
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
        var url = _currentEngine == "Gemini" ? GeminiUsageUrl : LangblyUsageUrl;
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
        if (_currentEngine == "MyMemory")     MyMemoryEmail = EmailBox.Text.Trim();
        else if (_currentEngine == "DeepL")   DeepLApiKey   = ApiKeyBox.Password;
        else if (_currentEngine == "Gemini")  GeminiApiKey  = ApiKeyBox.Password;
        else                                  LangblyApiKey = ApiKeyBox.Password;
    }
}
