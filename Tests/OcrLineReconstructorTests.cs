using AlctClient.Core;

namespace AlctClient.Tests;

public class OcrLineReconstructorTests
{
    // 서버 _box(left, top, width=100, height=20) 대응 — Left/Top/Right/Bottom
    private static OcrLineReconstructor.Fragment Frag(string text, double left, double top,
        double width = 100, double height = 20)
        => new(text, left, top, left + width, top + height);

    [Fact]
    public void SeparateRows_KeptAsSeparateLines_TopToBottom()
    {
        var lines = OcrLineReconstructor.ReconstructLines(new[]
        {
            Frag("third", 0, 80),
            Frag("first", 0, 0),
            Frag("second", 0, 40),
        });

        Assert.Equal(new[] { "first", "second", "third" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void SameRow_CloseBoxes_MergeIntoOneSegment_OrderedByX()
    {
        // 가로 간격이 작은(같은 문장) 두 박스 → 한 세그먼트, left→right
        var lines = OcrLineReconstructor.ReconstructLines(new[]
        {
            Frag("world", 108, 0),   // gap = 108 - 100 = 8 (작음)
            Frag("Hello", 0, 2),
        });

        var line = Assert.Single(lines);
        Assert.Equal("Hello world", line.Text);
        Assert.Equal(0, line.Left);
        Assert.Equal(208, line.Right);
    }

    [Fact]
    public void SameRow_FarBoxes_SplitIntoSeparateSegments()
    {
        // 가로 간격이 큰(별개 요소) 두 박스 → 각각의 박스로 분리
        var lines = OcrLineReconstructor.ReconstructLines(new[]
        {
            Frag("left", 0, 0),
            Frag("right", 300, 0),   // gap = 300 - 100 = 200 (큼)
        });

        Assert.Equal(2, lines.Count);
        Assert.Equal("left", lines[0].Text);
        Assert.Equal(0, lines[0].Left);
        Assert.Equal(100, lines[0].Right);
        Assert.Equal("right", lines[1].Text);
        Assert.Equal(300, lines[1].Left);
        Assert.Equal(400, lines[1].Right);
    }

    [Fact]
    public void MergedSegment_SpansItsOwnFragments()
    {
        var lines = OcrLineReconstructor.ReconstructLines(new[]
        {
            Frag("a", 0, 0, height: 20),
            Frag("b", 105, 2, height: 20),   // gap 5 < 0.75*22
        });

        var line = Assert.Single(lines);
        Assert.Equal("a b", line.Text);
        Assert.Equal(0, line.Top);
        Assert.Equal(22, line.Bottom);
    }

    [Fact]
    public void Empty_ReturnsNoLines()
    {
        Assert.Empty(OcrLineReconstructor.ReconstructLines(Array.Empty<OcrLineReconstructor.Fragment>()));
    }
}
