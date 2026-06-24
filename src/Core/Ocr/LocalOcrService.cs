using System.Drawing;
using AlctClient.Utils;

namespace AlctClient.Core;

// 로컬 OCR 파이프라인. 서버 OcrHttpClient를 대체. 동일한 OcrTextReceived(normalized, raw)
// 이벤트를 발생시켜 소비자(MainWindow.Caption InitOcrHandler)는 무변경으로 동작한다.
//
// 파이프라인(서버 extractText + handler 순서와 동일):
//   CyanMask(닉네임 제거) → 엔진 추론 → OcrLineReconstructor(줄 재구성) = rawText
//   → ChatSlangNormalizer(약어 <x> 태깅) = normalizedText
public sealed class LocalOcrService : IDisposable
{
    private readonly Lazy<IOcrEngine> _engine;

    public event Action<string, string>? OcrTextReceived;  // (normalizedText, rawText)

    public LocalOcrService() : this(CreateDefaultEngine) { }

    // 엔진 주입 — 테스트/교체용. 엔진 생성(모델 로드)은 첫 사용까지 지연하되 1회만 수행.
    internal LocalOcrService(Func<IOcrEngine> engineFactory)
        => _engine = new Lazy<IOcrEngine>(engineFactory, LazyThreadSafetyMode.ExecutionAndPublication);

    // 첫 핫키 지연을 없애기 위한 모델 사전 로드(백그라운드 fire-and-forget 호출 권장). 실패는 호출부가 처리.
    public void Warmup() => _ = _engine.Value;

    // bitmap은 제자리에서 마스킹됨 — 호출부가 소유/해제한다.
    public async Task RecognizeAsync(Bitmap bitmap)
    {
        var (normalized, raw) = await Task.Run(() =>
        {
            CyanMask.Apply(bitmap);
            var fragments = _engine.Value.Recognize(bitmap);
            var rawText = OcrLineReconstructor.Reconstruct(fragments);
            return (ChatSlangNormalizer.Normalize(rawText), rawText);
        });
        // 빈 결과(텍스트 미인식)여도 이벤트를 쏴서 "찾지 못함"을 안내할 수 있게 한다(서버와 동일).
        OcrTextReceived?.Invoke(normalized, raw);
    }

    // OneOCR(Snipping Tool 엔진) 우선 — 품질이 크게 좋음. 미설치/초기화 실패 시 PP-OCRv5로 폴백.
    // ALCT_DISABLE_ONEOCR 로 강제 폴백 가능(디버그/문제 회피용).
    private static IOcrEngine CreateDefaultEngine()
    {
        if (Environment.GetEnvironmentVariable("ALCT_DISABLE_ONEOCR") is not { Length: > 0 })
        {
            try
            {
                var engine = new OneOcrEngine();
                Logger.Info("Ocr", "Engine selected: OneOCR (Snipping Tool)");
                return engine;
            }
            catch (Exception ex)
            {
                Logger.Info("Ocr", $"OneOCR unavailable — falling back to PP-OCRv5: {ex.Message}");
            }
        }

        // 폴백: 임베드된 PP-OCRv5 mobile ch 모델(서버 alct-server 와 동일 조합)을 %APPDATA%로 추출 후 로드.
        var m = ModelStore.EnsureExtracted();
        Logger.Info("Ocr", "Engine selected: PP-OCRv5 (RapidOcrNet)");
        return new RapidOcrNetEngine(m.Det, m.Cls, m.Rec, m.Keys);
    }

    public void Dispose()
    {
        if (_engine.IsValueCreated) _engine.Value.Dispose();
    }
}
