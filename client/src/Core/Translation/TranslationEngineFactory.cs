namespace AlctClient.Core;

public static class TranslationEngineFactory
{
    public static ITranslationService Create(TranslationEngine engine, string apiKey = "")
    {
        ITranslationService inner = engine switch
        {
            TranslationEngine.DeepL   => new DeepLTranslationService(apiKey),
            TranslationEngine.Gemini  => new GeminiTranslationService(apiKey),
            TranslationEngine.Langbly => new LangblyTranslationService(apiKey),
            _                         => new MyMemoryTranslationService(),
        };
        return new GlossaryTranslationDecorator(inner, GlossaryService.Instance);
    }

    public static TranslationEngine Parse(string? raw) => raw switch
    {
        "DeepL"   => TranslationEngine.DeepL,
        "Gemini"  => TranslationEngine.Gemini,
        "Langbly" => TranslationEngine.Langbly,
        _         => TranslationEngine.MyMemory,  // "LibreTranslate" 등 구버전 값 포함
    };
}
