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

    // 백그라운드 핸들러(읽기) ↔ UI 스레드(키/엔진 변경 시 재할당) 교차 접근 → 최신 참조 가시성 보장 (GlossaryService와 동일)
    private volatile ITranslationService _voiceService;
    private volatile ITranslationService _textService;
    public ITranslationService VoiceService => _voiceService;
    public ITranslationService TextService => _textService;

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
        _voiceService = TranslationEngineFactory.Create(voiceEngine, GetCredential(voiceEngine));
        _textService  = TranslationEngineFactory.Create(textEngine,  GetCredential(textEngine));
    }

    // 엔진별 자격증명 — API 키(DeepL/Gemini/Langbly) 또는 MyMemory의 de 파라미터용 이메일
    public string GetCredential(TranslationEngine engine) => engine switch
    {
        TranslationEngine.DeepL      => _deepLKey,
        TranslationEngine.Gemini     => _geminiKey,
        TranslationEngine.GeminiLive => _geminiKey,  // Gemini와 같은 키를 공유(별도 트랙·동일 자격증명)
        TranslationEngine.Langbly    => _langblyKey,
        TranslationEngine.MyMemory   => _myMemoryEmail,
        _                            => string.Empty,
    };

    // 두 엔진이 같은 자격증명 필드를 쓰는가. Gemini·GeminiLive는 한 그룹(공유 키), 나머지는 각자 단독.
    // → Gemini 키를 저장하면 GeminiLive 슬롯도 함께 재생성해야 하므로 필요.
    private static bool SharesCredentialField(TranslationEngine a, TranslationEngine b) =>
        a == b
        || (a is TranslationEngine.Gemini or TranslationEngine.GeminiLive
            && b is TranslationEngine.Gemini or TranslationEngine.GeminiLive);

    // 키/이메일 변경: 해당 엔진을 쓰는 슬롯만 재생성. 활성 음성 엔진의 자격증명이 바뀌면 차단 해제(낙관적).
    // 새 자격증명은 새/상향 할당량일 수 있음(DeepL 새 키, MyMemory 이메일, Gemini는 별도 GCP 프로젝트 키 등).
    // 여전히 한도 초과면 다음 요청에서 엔진이 다시 차단하므로 안전.
    public void UpdateCredential(TranslationEngine engine, string credential)
    {
        SetCredential(engine, credential);
        // 같은 필드를 공유하는 슬롯만 재생성. (Gemini 키 변경 시 GeminiLive 슬롯도 포함) credential은 공유 필드 값과 동일.
        if (SharesCredentialField(_voiceEngine, engine))
        {
            SwapVoiceService(TranslationEngineFactory.Create(_voiceEngine, credential));
            _voiceQuotaBlockedUntil = DateTime.MinValue;
        }
        if (SharesCredentialField(_textEngine, engine))
            SwapTextService(TranslationEngineFactory.Create(_textEngine, credential));
    }

    public void SetVoiceEngine(TranslationEngine engine)
    {
        _voiceEngine = engine;
        SwapVoiceService(TranslationEngineFactory.Create(engine, GetCredential(engine)));
        _voiceQuotaBlockedUntil = DateTime.MinValue;  // 엔진 변경 = 새 할당량 컨텍스트
    }

    public void SetTextEngine(TranslationEngine engine)
    {
        _textEngine = engine;
        SwapTextService(TranslationEngineFactory.Create(engine, GetCredential(engine)));
    }

    // 슬롯 교체 — 이전 인스턴스가 소켓 등 자원을 쥐고 있으면(예: GeminiLive) 정리. 새 참조를 먼저 노출한 뒤 옛것 해제.
    private void SwapVoiceService(ITranslationService next)
    {
        var old = _voiceService;
        _voiceService = next;
        (old as IDisposable)?.Dispose();
    }

    private void SwapTextService(ITranslationService next)
    {
        var old = _textService;
        _textService = next;
        (old as IDisposable)?.Dispose();
    }

    // 음성 할당량 차단(한도 초과 시 재개 시각까지 요청 억제). 텍스트 경로엔 적용 안 함
    public bool IsVoiceQuotaBlocked => DateTime.UtcNow < _voiceQuotaBlockedUntil;
    public void BlockVoiceQuotaUntil(DateTime retryAtUtc) => _voiceQuotaBlockedUntil = retryAtUtc;

    private void SetCredential(TranslationEngine engine, string credential)
    {
        switch (engine)
        {
            case TranslationEngine.DeepL:      _deepLKey      = credential; break;
            case TranslationEngine.Gemini:     _geminiKey     = credential; break;
            case TranslationEngine.GeminiLive: _geminiKey     = credential; break;  // Gemini와 같은 키
            case TranslationEngine.Langbly:    _langblyKey    = credential; break;
            case TranslationEngine.MyMemory:   _myMemoryEmail = credential; break;
        }
    }
}
