using AlctClient.Core;

namespace AlctClient.Tests;

// 서버 test_ocr_service.py TestExtractText 의 줄 재구성 케이스를 이식.
public class OcrLineReconstructorTests
{
    // 서버 _box(left, top, width=100, height=20) 대응 — Top/Bottom/Left만 추출
    private static OcrLineReconstructor.Fragment Frag(string text, double left, double top,
        double width = 100, double height = 20)
        => new(text, left, top, top + height);

    [Fact]
    public void Reconstruct_SeparateRows_JoinedWithNewline()
    {
        var result = OcrLineReconstructor.Reconstruct(new[]
        {
            Frag("Hello", 0, 0),
            Frag("こんにちは", 0, 40),
        });

        Assert.Equal("Hello\nこんにちは", result);
    }

    [Fact]
    public void Reconstruct_SameRowBoxes_BecomeOneLineOrderedByX()
    {
        // 세로로 겹치는(같은 행) 두 박스 → 한 줄, left→right 순
        var result = OcrLineReconstructor.Reconstruct(new[]
        {
            Frag("world", 120, 0),
            Frag("Hello", 0, 2),
        });

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Reconstruct_OutOfOrderRows_SortedTopToBottom()
    {
        var result = OcrLineReconstructor.Reconstruct(new[]
        {
            Frag("third", 0, 80),
            Frag("first", 0, 0),
            Frag("second", 0, 40),
        });

        Assert.Equal("first\nsecond\nthird", result);
    }

    [Fact]
    public void Reconstruct_Empty_ReturnsEmptyString()
    {
        Assert.Equal("", OcrLineReconstructor.Reconstruct(Array.Empty<OcrLineReconstructor.Fragment>()));
    }
}
