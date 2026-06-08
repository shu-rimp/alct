namespace AlctClient.Core;

public interface ITranslationService
{
    Task<string> TranslateToKoreanAsync(string text, string sourceLang, CancellationToken ct = default);
    Task<string> TranslateFromKoreanAsync(string text, string targetLang);
    string MapLanguageCode(string bcp47);

    static string StripXmlTags(string text) =>
        text.Replace("<x>", "").Replace("</x>", "").Trim();
}

public enum TranslationEngine { MyMemory, DeepL, Gemini }
