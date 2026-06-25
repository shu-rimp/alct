using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AlctClient.Core;

// 게임 채팅 닉네임은 항상 cyan으로, 메시지는 흰색으로 렌더링된다. cyan 픽셀이 있는 각 행에서
// 가장 오른쪽 cyan 위치(+여백)까지 좌측 구간을 통째로 배경색으로 지워 닉네임 블록을 제거한다.
// 픽셀 단위 치환만으론 안티앨리어싱 잔상이 남아 OCR이 다시 잡으므로 좌측 세그먼트 전체를 지운다.
// 서버 alct-server/src/core/ocr_service.py 의 maskCyanText 포팅 — 임계값/여백 동일.
internal static class CyanMask
{
    private const int GB_MINUS_R_THRESHOLD = 70;  // (G-R), (B-R) 둘 다 초과해야 cyan
    private const int G_MIN = 50;                 // 순수 어두운 노이즈 제외용 최소 밝기
    private const int RIGHT_PADDING = 5;          // 마지막 cyan 열 뒤로 추가로 지울 픽셀
    private const byte BACKGROUND = 20;           // 배경색 [20, 20, 20]

    // 캡처 비트맵을 제자리에서 마스킹한다(곧 OCR에 소비되므로 사본 불필요 — 서버의 .copy()와 다른 점).
    public static void Apply(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            for (var y = 0; y < data.Height; y++)
            {
                var rowStart = y * data.Stride;
                var lastCyanX = -1;
                for (var x = 0; x < data.Width; x++)
                {
                    var p = rowStart + x * 4;  // 메모리 순서: B, G, R, A
                    int b = buffer[p], g = buffer[p + 1], r = buffer[p + 2];
                    if (g - r > GB_MINUS_R_THRESHOLD && b - r > GB_MINUS_R_THRESHOLD && g > G_MIN)
                        lastCyanX = x;
                }
                if (lastCyanX < 0) continue;

                var maskEnd = Math.Min(lastCyanX + RIGHT_PADDING, data.Width);  // [0, maskEnd) 제거
                for (var x = 0; x < maskEnd; x++)
                {
                    var p = rowStart + x * 4;
                    buffer[p] = BACKGROUND;      // B
                    buffer[p + 1] = BACKGROUND;  // G
                    buffer[p + 2] = BACKGROUND;  // R — alpha(p+3)는 유지
                }
            }
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
