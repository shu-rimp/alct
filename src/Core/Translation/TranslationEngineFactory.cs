namespace AlctClient.Core;

public static class TranslationEngineFactory
{
    public static ITranslationService Create(TranslationEngine engine, string apiKey = "")
        => engine switch
        {
            TranslationEngine.DeepL  => new DeepLTranslationService(apiKey),
            TranslationEngine.Gemini => new GeminiTranslationService(apiKey),
            _                        => new MyMemoryTranslationService(),
        };

    public static TranslationEngine Parse(string? raw) => raw switch
    {
        "DeepL"  => TranslationEngine.DeepL,
        "Gemini" => TranslationEngine.Gemini,
        _        => TranslationEngine.MyMemory,  // "LibreTranslate" 등 구버전 값 포함
    };
}
