using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AlctClient.Core;

// Snipping Tool 내장 OCR 엔진(oneocr.dll) 어댑터 — 기본(우선) 엔진. 미설치/초기화 실패 시
// LocalOcrService가 RapidOcrNet(PP-OCRv5)로 폴백한다.
//
// 비문서화 MS 엔진을 리버스엔지니어링 인터페이스로 호출한다. oneocr.dll/onemodel/onnxruntime.dll 은
// 사용자 PC의 Microsoft.ScreenSketch(Snipping Tool)에서 런타임 추출해 로드하며 ALCT는 재배포하지 않는다.
// 키/시그니처가 MS 업데이트로 바뀌면 초기화가 실패할 수 있으나, 그 경우 RapidOcr 폴백으로 흡수된다.
internal sealed class OneOcrEngine : IOcrEngine
{
    // oneocr.dll 파이프라인 초기화 매직 키(알려진 MS 내부 상수)
    private const string KEY = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

    // 설치 경로가 읽기전용(Program Files)일 수 있어 쓰기 가능한 %APPDATA% 하위에 추출한다.
    private static readonly string DllDir = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ALCT", "OneOcrDLL");

    private readonly long _pipeline;
    private readonly long _opt;

    public OneOcrEngine()
    {
        EnsureNativeFiles();
        SetDllDirectory(DllDir);  // oneocr.dll + 의존 onnxruntime.dll 탐색 경로 등록

        long res = Native.CreateOcrInitOptions(out var ctx);
        if (res != 0) throw new InvalidOperationException($"CreateOcrInitOptions failed: {res}");
        Native.OcrInitOptionsSetUseModelDelayLoad(ctx, 0);

        var modelPath = IOPath.Combine(DllDir, "oneocr.onemodel");
        res = Native.CreateOcrPipeline(modelPath, KEY, ctx, out _pipeline);
        if (res != 0) res = Native.CreateOcrPipeline_Utf16(modelPath, KEY, ctx, out _pipeline);
        if (res != 0) throw new InvalidOperationException($"CreateOcrPipeline failed: {res}");

        res = Native.CreateOcrProcessOptions(out _opt);
        if (res != 0) throw new InvalidOperationException($"CreateOcrProcessOptions failed: {res}");
        Native.OcrProcessOptionsSetMaxRecognitionLineCount(_opt, 1000);
    }

    public IReadOnlyList<OcrLineReconstructor.Fragment> Recognize(Bitmap bitmap)
    {
        // oneocr는 24bpp(BGR) 버퍼 + step(stride)을 받는다. 마스킹된 비트맵을 24bpp로 변환해 전달.
        using var rgb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(rgb)) g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);

        var data = rgb.LockBits(new Rectangle(0, 0, rgb.Width, rgb.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var img = new Img { t = 1, col = rgb.Width, row = rgb.Height, _unk = 0, step = data.Stride, data_ptr = data.Scan0 };
            if (Native.RunOcrPipeline(_pipeline, ref img, _opt, out var instance) != 0)
                return Array.Empty<OcrLineReconstructor.Fragment>();
            if (Native.GetOcrLineCount(instance, out var lineCount) != 0)
                return Array.Empty<OcrLineReconstructor.Fragment>();

            var fragments = new List<OcrLineReconstructor.Fragment>();
            for (long i = 0; i < lineCount; i++)
            {
                if (Native.GetOcrLine(instance, i, out var line) != 0 || line == 0) continue;
                if (Native.GetOcrLineContent(line, out var contentPtr) != 0 || contentPtr == IntPtr.Zero) continue;
                if (Native.GetOcrLineBoundingBox(line, out var boxPtr) != 0 || boxPtr == IntPtr.Zero) continue;

                var text = Marshal.PtrToStringUTF8(contentPtr) ?? string.Empty;
                var box = Marshal.PtrToStructure<BoundingBox>(boxPtr);
                var minX = Math.Min(Math.Min(box.x1, box.x2), Math.Min(box.x3, box.x4));
                var maxX = Math.Max(Math.Max(box.x1, box.x2), Math.Max(box.x3, box.x4));
                var minY = Math.Min(Math.Min(box.y1, box.y2), Math.Min(box.y3, box.y4));
                var maxY = Math.Max(Math.Max(box.y1, box.y2), Math.Max(box.y3, box.y4));
                fragments.Add(new OcrLineReconstructor.Fragment(text, minX, minY, maxX, maxY));
            }
            return fragments;
        }
        finally { rgb.UnlockBits(data); }
    }

    // Snipping Tool 설치 위치에서 네이티브 파일 3종 + 모델을 OneOcrDLL 폴더로 1회 복사
    private static void EnsureNativeFiles()
    {
        if (IOFile.Exists(IOPath.Combine(DllDir, "oneocr.dll"))) return;
        var src = FindSnippingToolDir() ?? throw new InvalidOperationException("Snipping Tool(oneocr) not found");
        System.IO.Directory.CreateDirectory(DllDir);
        foreach (var f in new[] { "oneocr.dll", "oneocr.onemodel", "onnxruntime.dll" })
            IOFile.Copy(IOPath.Combine(src, f), IOPath.Combine(DllDir, f), overwrite: true);
    }

    private static string? FindSnippingToolDir()
    {
        var info = new ProcessStartInfo("powershell.exe",
            "-NoProfile -Command \"(Get-AppxPackage -Name *ScreenSketch*).InstallLocation\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = Process.Start(info)!;
        var root = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        if (string.IsNullOrEmpty(root)) return null;
        var dir = IOPath.Combine(root, "SnippingTool");
        return IOFile.Exists(IOPath.Combine(dir, "oneocr.dll")) ? dir : null;
    }

    public void Dispose() { /* oneocr가 해제 API를 노출하지 않아 프로세스 종료에 맡김 */ }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Img { public int t; public int col; public int row; public int _unk; public long step; public IntPtr data_ptr; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundingBox { public float x1, y1, x2, y2, x3, y3, x4, y4; }

    // oneocr.dll exports (Cdecl). 
    private static class Native
    {
        private const string DLL = "oneocr.dll";
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long CreateOcrInitOptions(out long ctx);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long OcrInitOptionsSetUseModelDelayLoad(long ctx, byte flag);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long CreateOcrPipeline(string modelPath, string key, long ctx, out long pipeline);
        [DllImport(DLL, EntryPoint = "CreateOcrPipeline", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)] public static extern long CreateOcrPipeline_Utf16(string modelPath, string key, long ctx, out long pipeline);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long CreateOcrProcessOptions(out long opt);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long OcrProcessOptionsSetMaxRecognitionLineCount(long opt, long count);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long RunOcrPipeline(long pipeline, ref Img img, long opt, out long instance);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long GetOcrLineCount(long instance, out long count);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long GetOcrLine(long instance, long index, out long line);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long GetOcrLineContent(long line, out IntPtr content);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern long GetOcrLineBoundingBox(long line, out IntPtr boundingBoxPtr);
    }
}
