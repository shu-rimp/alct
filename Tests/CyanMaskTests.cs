using System.Drawing;
using System.Drawing.Imaging;
using AlctClient.Core;

namespace AlctClient.Tests;

// 서버 test_ocr_service.py TestMaskCyanText 의 케이스를 이식.
public class CyanMaskTests
{
    private static bool IsCyan(Color c) => c.G - c.R > 70 && c.B - c.R > 70 && c.G > 50;

    // 서버 _makeImageWithCyan: 60x200, rows[10:30] cols[0:60]=cyan(R0,G210,B210), cols[80:160]=white
    private static Bitmap MakeImageWithCyan()
    {
        var bmp = new Bitmap(200, 60, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(20, 20, 20));
        using (var cyan = new SolidBrush(Color.FromArgb(0, 210, 210)))
            g.FillRectangle(cyan, 0, 10, 60, 20);     // x[0,60) y[10,30)
        using (var white = new SolidBrush(Color.FromArgb(255, 255, 255)))
            g.FillRectangle(white, 80, 10, 80, 20);   // x[80,160) y[10,30)
        return bmp;
    }

    [Fact]
    public void Apply_RemovesAllCyanPixels()
    {
        using var img = MakeImageWithCyan();

        CyanMask.Apply(img);

        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                Assert.False(IsCyan(img.GetPixel(x, y)), $"cyan remained at ({x},{y})");
    }

    [Fact]
    public void Apply_PreservesWhiteText()
    {
        using var img = MakeImageWithCyan();

        CyanMask.Apply(img);

        // 흰색 블록은 마스킹 구간(cols 0..64) 바깥이라 보존돼야 함
        for (var y = 10; y < 30; y++)
            for (var x = 90; x < 160; x++)
                Assert.Equal(Color.FromArgb(255, 255, 255).ToArgb(), img.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void Apply_DarkBackgroundUnchanged()
    {
        using var img = new Bitmap(50, 50, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(img)) g.Clear(Color.FromArgb(20, 20, 20));

        CyanMask.Apply(img);

        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                Assert.Equal(Color.FromArgb(20, 20, 20).ToArgb(), img.GetPixel(x, y).ToArgb());
    }
}
