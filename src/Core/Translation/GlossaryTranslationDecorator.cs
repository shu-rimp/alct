using AlctClient.Utils;

namespace AlctClient.Core;

// 번역 요청 직전에 용어집 치환을 적용하는 데코레이터 — 엔진 종류와 무관하게 동작
public sealed class GlossaryTranslationDecorator : ITranslationService
{
    private readonly ITranslationService _inner;
    private readonly GlossaryService _glossary;

    public GlossaryTranslationDecorator(ITranslationService inner, GlossaryService glossary)
    {
        _inner = inner;
        _glossary = glossary;
    }

    public Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
    {
        var applied = _glossary.Apply(text, sourceLang);
        // 디버깅 용 로그 — STT/OCR이 실제로 출력한 표기를 코드포인트까지 기록 (들리는 발음과 다른 경우가 많음). 필요시 해제
        // var codepoints = string.Join(" ", text.Take(40).Select(c => ((int)c).ToString("X4")));
        // Logger.Info("Glossary", $"lang={sourceLang} | in=[{text}] | out=[{applied}] | cp={codepoints}");
        return _inner.TranslateToKoreanAsync(applied, sourceLang, context, ct);
    }

    // 한국어 → 외국어 입력 번역은 용어집 대상 아님
    public Task<string> TranslateFromKoreanAsync(string text, string targetLang)
        => _inner.TranslateFromKoreanAsync(text, targetLang);

    public string MapLanguageCode(string bcp47) => _inner.MapLanguageCode(bcp47);
}
