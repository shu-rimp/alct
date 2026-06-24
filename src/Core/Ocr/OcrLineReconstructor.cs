namespace AlctClient.Core;

// RapidOCR은 한 시각적 줄을 여러 검출 박스로 쪼개는 경우가 많아, 박스마다 줄바꿈하면 한 줄이
// 여러 줄로 깨진다. 세로로 겹치는(overlap > 높이의 50%) 박스들을 같은 행으로 묶어 한 줄로
// 합치고(행 내 left→right 정렬), 진짜 다른 행끼리만 줄바꿈한다(행 간 top→bottom 정렬).
// 서버 alct-server/src/core/ocr_service.py 의 _reconstructLines 포팅.
internal static class OcrLineReconstructor
{
    // OCR 박스 하나. Top/Bottom = 박스 y좌표의 min/max, Left = x좌표의 min(서버와 동일 산식).
    public readonly record struct Fragment(string Text, double Left, double Top, double Bottom);

    public static string Reconstruct(IReadOnlyList<Fragment> fragments)
    {
        var items = fragments.OrderBy(f => f.Top).ToList();  // 안정 정렬 — 동일 top 시 입력 순서 보존
        var lines = new List<Line>();

        foreach (var item in items)
        {
            var target = lines.FirstOrDefault(line =>
            {
                var overlap = Math.Min(line.Bottom, item.Bottom) - Math.Max(line.Top, item.Top);
                var minHeight = Math.Min(line.Bottom - line.Top, item.Bottom - item.Top);
                return minHeight > 0 && overlap > minHeight * 0.5;
            });

            if (target is null)
            {
                lines.Add(new Line(item.Top, item.Bottom, item.Left, item.Text));
            }
            else
            {
                target.Parts.Add((item.Left, item.Text));
                target.Top = Math.Min(target.Top, item.Top);
                target.Bottom = Math.Max(target.Bottom, item.Bottom);
            }
        }

        return string.Join("\n",
            lines.Select(line => string.Join(" ",
                line.Parts.OrderBy(p => p.Left).Select(p => p.Text))));
    }

    private sealed class Line
    {
        public double Top;
        public double Bottom;
        public readonly List<(double Left, string Text)> Parts;

        public Line(double top, double bottom, double left, string text)
        {
            Top = top;
            Bottom = bottom;
            Parts = new List<(double, string)> { (left, text) };
        }
    }
}
