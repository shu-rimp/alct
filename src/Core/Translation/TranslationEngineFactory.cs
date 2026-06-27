namespace AlctClient.Core;

public static class TranslationEngineFactory
{
    // 기본 번역 엔진 — 키 없이 바로 쓸 수 있는 MyMemory. 엔진 기본값의 단일 출처.
    public const TranslationEngine Default = TranslationEngine.MyMemory;

    public static ITranslationService Create(TranslationEngine engine, string apiKey = "")
    {
        ITranslationService inner = engine switch
        {
            TranslationEngine.DeepL      => new DeepLTranslationService(apiKey),
            TranslationEngine.Gemini     => new GeminiTranslationService(apiKey),
            TranslationEngine.GeminiLive => new GeminiLiveTranslationService(apiKey),
            TranslationEngine.Langbly    => new LangblyTranslationService(apiKey),
            _                         => new MyMemoryTranslationService(apiKey),  // 이메일(de 파라미터)을 전달받음
        };
        return new GlossaryTranslationDecorator(inner, GlossaryService.Instance);
    }

    public static TranslationEngine Parse(string? raw) => raw switch
    {
        "DeepL"      => TranslationEngine.DeepL,
        "Gemini"     => TranslationEngine.Gemini,
        "GeminiLive" => TranslationEngine.GeminiLive,
        "Langbly"    => TranslationEngine.Langbly,
        _         => Default,
    };
}
