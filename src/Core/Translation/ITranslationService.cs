namespace AlctClient.Core;

public interface ITranslationService
{
    // context: 직전 발화들(원어) — 짧은 문장의 번역 정확도를 높이는 참고용, 번역 대상 아님
    Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default);
    Task<string> TranslateFromKoreanAsync(string text, string targetLang);
    string MapLanguageCode(string bcp47);

    static string StripXmlTags(string text) =>
        text.Replace("<x>", "").Replace("</x>", "").Trim();
}

public enum TranslationEngine { MyMemory, DeepL, Gemini, Langbly }

// 번역 엔진의 사용량/속도 제한 초과. RetryAtUtc는 재개 가능 시각(엔진이 응답에서 산출, 폴백 포함)
public sealed class TranslationRateLimitException : Exception
{
    public DateTime RetryAtUtc { get; }

    public TranslationRateLimitException(string message, DateTime retryAtUtc) : base(message)
        => RetryAtUtc = retryAtUtc;
}
