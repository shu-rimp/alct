using System.Drawing;
using AlctClient.Core;

namespace AlctClient.Tests;

public class ScreenCaptureServiceTests
{
    [Fact]
    public void ScaleRegionToScreen_AtFhd_ReturnsBaseRegion()
    {
        // 1920x1080에서는 스케일 1.0 → FHD 기준 영역 그대로
        var region = ScreenCaptureService.ScaleRegionToScreen(new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new Rectangle(50, 433, 600, 250), region);
    }

    [Fact]
    public void ScaleRegionToScreen_AtQhd_ScalesProportionally()
    {
        // 2560x1440 → 가로·세로 모두 1.3333배
        var region = ScreenCaptureService.ScaleRegionToScreen(new Rectangle(0, 0, 2560, 1440));

        Assert.Equal(67, region.X);        // 50  * 4/3 = 66.67 → 67
        Assert.Equal(577, region.Y);       // 433 * 4/3 = 577.33 → 577
        Assert.Equal(800, region.Width);   // 600 * 4/3 = 800
        Assert.Equal(333, region.Height);  // 250 * 4/3 = 333.33 → 333
    }

    [Fact]
    public void ScaleRegionToScreen_OffsetScreen_AddsBoundsOrigin()
    {
        // 멀티모니터: 보조 화면 원점(X/Y 오프셋)이 더해진다
        var region = ScreenCaptureService.ScaleRegionToScreen(new Rectangle(1920, 0, 1920, 1080));

        Assert.Equal(1970, region.X);  // 1920 + 50
        Assert.Equal(433, region.Y);
    }

    [Fact]
    public void GetCaptureRegion_CustomRegion_ReturnsProvided()
    {
        var custom = new Rectangle(10, 20, 300, 150);
        var service = new ScreenCaptureService(custom);

        Assert.Equal(custom, service.GetCaptureRegion());
    }

    [Fact]
    public void EncodeToPng_Bitmap_ReturnsPngBytes()
    {
        using var bitmap = new Bitmap(10, 10);
        var bytes = ScreenCaptureService.EncodeToPng(bitmap);

        // PNG magic number: 0x89 0x50 0x4E 0x47
        Assert.True(bytes.Length > 4);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]); // 'P'
        Assert.Equal(0x4E, bytes[2]); // 'N'
        Assert.Equal(0x47, bytes[3]); // 'G'
    }

    [Fact]
    public void EncodeToPng_NonEmptyBitmap_HasNonZeroSize()
    {
        using var bitmap = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bitmap);
        g.FillRectangle(System.Drawing.Brushes.Red, 0, 0, 100, 100);

        var bytes = ScreenCaptureService.EncodeToPng(bitmap);
        Assert.NotEmpty(bytes);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(200, 100)]
    public void EncodeToPng_VariousSizes_AlwaysReturnsPng(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        var bytes = ScreenCaptureService.EncodeToPng(bitmap);

        Assert.True(bytes.Length > 0);
        Assert.Equal(0x89, bytes[0]);
    }
}
