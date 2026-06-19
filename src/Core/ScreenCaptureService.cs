using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AlctClient.Core;

public class ScreenCaptureService
{
    // chat area on 1920x1080 — base reference for proportional scaling.
    // Tall enough to span both vertical positions: 3-player (higher) and 1/2-player (lower).
    private static readonly Rectangle FHD_CAPTURE_REGION = new(50, 433, 600, 250);

    private Rectangle _captureRegion;

    public ScreenCaptureService() : this(GetDefaultCaptureRegion()) { }

    public void SetCaptureRegion(Rectangle region) => _captureRegion = region;

    public static Rectangle GetDefaultCaptureRegion()
        => GetDefaultCaptureRegion(System.Windows.Forms.Screen.PrimaryScreen!);

    public static Rectangle GetDefaultCaptureRegion(System.Windows.Forms.Screen screen)
        => ScaleRegionToScreen(screen.Bounds);

    // FHD 기준 영역을 화면 해상도에 비례해 스케일. Screen 의존이 없는 순수 함수라 단위 테스트 가능.
    internal static Rectangle ScaleRegionToScreen(Rectangle screenBounds)
    {
        double sx = (double)screenBounds.Width  / 1920;
        double sy = (double)screenBounds.Height / 1080;
        return new Rectangle(
            screenBounds.X + (int)Math.Round(FHD_CAPTURE_REGION.X * sx),
            screenBounds.Y + (int)Math.Round(FHD_CAPTURE_REGION.Y * sy),
            (int)Math.Round(FHD_CAPTURE_REGION.Width  * sx),
            (int)Math.Round(FHD_CAPTURE_REGION.Height * sy));
    }

    public ScreenCaptureService(Rectangle captureRegion)
    {
        _captureRegion = captureRegion;
    }

    public byte[] CaptureRegionAsPng()
    {
        using var bitmap = CaptureRegion(_captureRegion);
        return EncodeToPng(bitmap);
    }

    public byte[] CaptureRegionAsPng(Rectangle region)
    {
        using var bitmap = CaptureRegion(region);
        return EncodeToPng(bitmap);
    }

    public Rectangle GetCaptureRegion() => _captureRegion;

    internal Bitmap CaptureRegion(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size);
            return bitmap;
        }
        catch  // CopyFromScreen 등 실패 시 bitmap 누수 방지 후 재던짐
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static byte[] EncodeToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
