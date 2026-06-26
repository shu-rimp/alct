namespace AlctClient.Core;

public interface ITranslationService
{
    // context: 직전 발화들(원어) — 짧은 문장의 번역 정확도를 높이는 참고용, 번역 대상 아님
    Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default);
    Task<string> TranslateFromKoreanAsync(string text, string targetLang);

    // 여러 텍스트(채팅 오버레이의 줄별 영역)를 한국어로 번역. 결과는 입력과 같은 개수·순서로 1:1 대응한다.
    // 대량 입력은 엔진별 안전 한도(분당/출력토큰/쿼리길이)에 맞춰 내부에서 나눠 보낸다.
    Task<IReadOnlyList<string>> TranslateBatchToKoreanAsync(IReadOnlyList<string> texts, string sourceLang, CancellationToken ct = default);

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
