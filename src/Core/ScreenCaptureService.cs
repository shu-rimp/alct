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
    {
        double sx = (double)screen.Bounds.Width  / 1920;
        double sy = (double)screen.Bounds.Height / 1080;
        return new Rectangle(
            screen.Bounds.X + (int)Math.Round(FHD_CAPTURE_REGION.X * sx),
            screen.Bounds.Y + (int)Math.Round(FHD_CAPTURE_REGION.Y * sy),
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
