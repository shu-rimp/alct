using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AlctClient.Core;

public class ScreenCaptureService
{
    // Apex Legends chat area on 1920x1080
    private static readonly Rectangle DEFAULT_CAPTURE_REGION = new(50, 513, 600, 167);

    private readonly Rectangle _captureRegion;

    public ScreenCaptureService() : this(DEFAULT_CAPTURE_REGION) { }

    public ScreenCaptureService(Rectangle captureRegion)
    {
        _captureRegion = captureRegion;
    }

    public byte[] CaptureRegionAsPng()
    {
        using var bitmap = CaptureRegion(_captureRegion);
        return EncodeToPng(bitmap);
    }

    public Rectangle GetCaptureRegion() => _captureRegion;

    internal Bitmap CaptureRegion(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Location, Point.Empty, region.Size);
        return bitmap;
    }

    internal static byte[] EncodeToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
