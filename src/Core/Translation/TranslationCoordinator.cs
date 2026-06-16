namespace AlctClient.Core;

// MainWindow에 흩어져 있던 번역 상태(엔진별 자격증명·엔진 선택·서비스 인스턴스·음성 할당량 차단)를 한 곳에 모은다.
// 순수 상태+로직이라 WPF/스레드 의존 없음 — "필드 갱신 → 영향받는 슬롯 재생성 → 할당량 차단 해제"의
// Settings/Onboarding 중복을 단일 진입점으로 통합. (저장은 호출부 책임)
//
// 슬롯은 둘: 음성(voice)·텍스트(text). 각 슬롯은 독립적으로 엔진을 고를 수 있고, 같은 엔진을 공유할 수도 있다.
public sealed class TranslationCoordinator
{
    private string _deepLKey;
    private string _geminiKey;
    private string _langblyKey;
    private string _myMemoryEmail;
    private TranslationEngine _voiceEngine;
    private TranslationEngine _textEngine;
    private DateTime _voiceQuotaBlockedUntil = DateTime.MinValue;  // 이 UTC 시각까지 음성 번역 요청 차단

    public ITranslationService VoiceService { get; private set; }
    public ITranslationService TextService { get; private set; }

    public TranslationCoordinator(
        TranslationEngine voiceEngine, TranslationEngine textEngine,
        string deepLKey, string geminiKey, string langblyKey, string myMemoryEmail)
    {
        _voiceEngine   = voiceEngine;
        _textEngine    = textEngine;
        _deepLKey      = deepLKey;
        _geminiKey     = geminiKey;
        _langblyKey    = langblyKey;
        _myMemoryEmail = myMemoryEmail;
        VoiceService = TranslationEngineFactory.Create(voiceEngine, GetCredential(voiceEngine));
        TextService  = TranslationEngineFactory.Create(textEngine,  GetCredential(textEngine));
    }

    // 엔진별 자격증명 — API 키(DeepL/Gemini/Langbly) 또는 MyMemory의 de 파라미터용 이메일
    public string GetCredential(TranslationEngine engine) => engine switch
    {
        TranslationEngine.DeepL    => _deepLKey,
        TranslationEngine.Gemini   => _geminiKey,
        TranslationEngine.Langbly  => _langblyKey,
        TranslationEngine.MyMemory => _myMemoryEmail,
        _                          => string.Empty,
    };

    // 키/이메일 변경: 해당 엔진을 쓰는 슬롯만 재생성. DeepL/MyMemory는 새 자격증명=새(상향) 할당량이라 음성 차단 해제.
    // (Gemini/Langbly는 기존 동작대로 차단을 유지 — 키 교체가 할당량 컨텍스트를 바꾸지 않음)
    public void UpdateCredential(TranslationEngine engine, string credential)
    {
        SetCredential(engine, credential);
        if (_voiceEngine == engine)
        {
            VoiceService = TranslationEngineFactory.Create(engine, credential);
            if (engine is TranslationEngine.DeepL or TranslationEngine.MyMemory)
                _voiceQuotaBlockedUntil = DateTime.MinValue;
        }
        if (_textEngine == engine)
            TextService = TranslationEngineFactory.Create(engine, credential);
    }

    public void SetVoiceEngine(TranslationEngine engine)
    {
        _voiceEngine = engine;
        VoiceService = TranslationEngineFactory.Create(engine, GetCredential(engine));
        _voiceQuotaBlockedUntil = DateTime.MinValue;  // 엔진 변경 = 새 할당량 컨텍스트
    }

    public void SetTextEngine(TranslationEngine engine)
    {
        _textEngine = engine;
        TextService = TranslationEngineFactory.Create(engine, GetCredential(engine));
    }

    // 음성 할당량 차단(한도 초과 시 재개 시각까지 요청 억제). 텍스트 경로엔 적용 안 함
    public bool IsVoiceQuotaBlocked => DateTime.UtcNow < _voiceQuotaBlockedUntil;
    public void BlockVoiceQuotaUntil(DateTime retryAtUtc) => _voiceQuotaBlockedUntil = retryAtUtc;

    private void SetCredential(TranslationEngine engine, string credential)
    {
        switch (engine)
        {
            case TranslationEngine.DeepL:    _deepLKey      = credential; break;
            case TranslationEngine.Gemini:   _geminiKey     = credential; break;
            case TranslationEngine.Langbly:  _langblyKey    = credential; break;
            case TranslationEngine.MyMemory: _myMemoryEmail = credential; break;
        }
    }
}
