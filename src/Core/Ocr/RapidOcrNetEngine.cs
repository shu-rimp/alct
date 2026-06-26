using System.Drawing;
using System.Drawing.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace AlctClient.Core;

// RapidOcrNet(ONNX) 어댑터. 서버 alct-server 와 동일한 PP-OCRv5 mobile ch 모델을 로드하고,
// Python rapidocr 패리티 프리셋(PythonCompat)으로 추론한다. CPU 스파이크 억제를 위해 ONNX
// intra/inter-op 스레드를 1로 고정(서버 ONNX_NUM_THREADS=1 과 동일).
internal sealed class RapidOcrNetEngine : IOcrEngine
{
    private const int NUM_THREAD = 1;

    private readonly RapidOcr _ocr;

    public RapidOcrNetEngine(string detPath, string clsPath, string recPath, string keysPath)
    {
        _ocr = new RapidOcr();
        using var sessionOptions = RapidOcr.GetDefaultSessionOptions(NUM_THREAD);
        _ocr.InitModels(detPath, clsPath, recPath, keysPath, sessionOptions);
    }

    public IReadOnlyList<OcrLineReconstructor.Fragment> Recognize(Bitmap bitmap)
    {
        using var skBitmap = ToSkBitmap(bitmap);
        var result = _ocr.Detect(skBitmap, RapidOcrOptions.PythonCompat);  // 서버 Python rapidocr 패리티

        if (result.TextBlocks is null)
            return Array.Empty<OcrLineReconstructor.Fragment>();

        return result.TextBlocks.Select(block =>
        {
            // BoxPoints: 폴리곤 4각(시계방향). 서버 _reconstructLines 와 동일하게 min/max로 줄인다.
            var xs = block.BoxPoints.Select(p => (double)p.X).ToArray();
            var ys = block.BoxPoints.Select(p => (double)p.Y).ToArray();
            return new OcrLineReconstructor.Fragment(block.Text, xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }).ToList();
    }

    private static SKBitmap ToSkBitmap(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);  // 마스킹된 비트맵을 SkiaSharp로 넘기는 one-shot 변환
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    public void Dispose() => _ocr.Dispose();
}
