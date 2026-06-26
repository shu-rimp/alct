using System.Drawing;
using AlctClient.Utils;

namespace AlctClient.Core;

// 인식된 한 줄 — 번역할 텍스트(약어 <x> 태깅 적용) + 캡처 영역 기준 박스(픽셀).
// 오버레이가 이 박스 위에 번역문을 그린다.
public readonly record struct OcrRegion(string Text, double Left, double Top, double Right, double Bottom);

// 로컬 OCR 파이프라인. 서버 OcrHttpClient를 대체.
//
// 파이프라인:
//   엔진 추론 → OcrLineReconstructor(줄 재구성) → 줄별 ChatSlangNormalizer(약어 <x> 태깅)
//   → 줄별 박스를 살린 OcrRegion 목록
public sealed class LocalOcrService : IDisposable
{
    private readonly Lazy<IOcrEngine> _engine;

    public event Action<IReadOnlyList<OcrRegion>>? OcrRegionsReceived;

    public LocalOcrService() : this(CreateDefaultEngine) { }

    // 엔진 주입 — 테스트/교체용. 엔진 생성(모델 로드)은 첫 사용까지 지연하되 1회만 수행.
    internal LocalOcrService(Func<IOcrEngine> engineFactory)
        => _engine = new Lazy<IOcrEngine>(engineFactory, LazyThreadSafetyMode.ExecutionAndPublication);

    // 첫 핫키 지연을 없애기 위한 모델 사전 로드(백그라운드 fire-and-forget 호출 권장). 실패는 호출부가 처리.
    public void Warmup() => _ = _engine.Value;

    // 호출부가 bitmap을 소유/해제한다.
    public async Task RecognizeAsync(Bitmap bitmap)
    {
        var regions = await Task.Run(() =>
        {
            var fragments = _engine.Value.Recognize(bitmap);
            return OcrLineReconstructor.ReconstructLines(fragments)
                .Select(l => new OcrRegion(
                    ChatSlangNormalizer.Normalize(l.Text), l.Left, l.Top, l.Right, l.Bottom))
                .ToList();
        });
        // 빈 결과(텍스트 미인식)여도 이벤트를 쏴서 "찾지 못함"을 안내할 수 있게 한다.
        OcrRegionsReceived?.Invoke(regions);
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
