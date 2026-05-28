using System.Drawing;
using AlctClient.Core;

namespace AlctClient.Tests;

public class ScreenCaptureServiceTests
{
    [Fact]
    public void GetCaptureRegion_DefaultRegion_HasExpectedDimensions()
    {
        var service = new ScreenCaptureService();
        var region = service.GetCaptureRegion();

        Assert.Equal(50, region.X);
        Assert.Equal(513, region.Y);
        Assert.Equal(600, region.Width);
        Assert.Equal(167, region.Height);
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
