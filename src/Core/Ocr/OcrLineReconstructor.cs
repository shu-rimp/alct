namespace AlctClient.Core;

// RapidOCR은 한 시각적 줄을 여러 검출 박스로 쪼개는 경우가 많아, 박스마다 줄바꿈하면 한 줄이
// 여러 줄로 깨진다. 세로로 겹치는(overlap > 높이의 50%) 박스들을 같은 행으로 묶는다.
// 단, 같은 행이라도 가로 간격이 큰(별개 UI 요소·열) 조각은 별도 박스로 분리한다 — 원문 위에
// 짧은 박스로 얹어 읽기 쉽게 하기 위함. 서버 alct-server/src/core/ocr_service.py 의 _reconstructLines 변형.
internal static class OcrLineReconstructor
{
    // OCR 박스 하나. Top/Bottom/Left/Right = 박스 좌표의 min/max.
    public readonly record struct Fragment(string Text, double Left, double Top, double Right, double Bottom);

    // 재구성된 한 세그먼트. 합쳐진 텍스트 + 박스(캡처 영역 기준 픽셀).
    public readonly record struct OcrLine(string Text, double Left, double Top, double Right, double Bottom);

    // 같은 행 안에서 가로 간격이 이 비율(줄 높이 대비)보다 크면 별개 세그먼트로 나눈다.
    // 단어 사이 공백(~0.3x)은 합치고, 열/별개 요소 사이의 큰 간격은 분리한다.
    private const double SEGMENT_GAP_RATIO = 0.75;

    public static IReadOnlyList<OcrLine> ReconstructLines(IReadOnlyList<Fragment> fragments)
    {
        var items = fragments.OrderBy(f => f.Top).ToList();  // 안정 정렬 — 동일 top 시 입력 순서 보존
        var rows = new List<Row>();

        foreach (var item in items)
        {
            var target = rows.FirstOrDefault(row =>
            {
                var overlap = Math.Min(row.Bottom, item.Bottom) - Math.Max(row.Top, item.Top);
                var minHeight = Math.Min(row.Bottom - row.Top, item.Bottom - item.Top);
                return minHeight > 0 && overlap > minHeight * 0.5;
            });

            if (target is null) rows.Add(new Row(item));
            else target.Add(item);
        }

        return rows.SelectMany(r => r.ToSegments(SEGMENT_GAP_RATIO))
            .OrderBy(l => l.Top).ThenBy(l => l.Left)  // 위→아래, 같은 행은 좌→우
            .ToList();
    }

    private sealed class Row
    {
        public double Top;
        public double Bottom;
        private readonly List<Fragment> _parts;

        public Row(Fragment first)
        {
            Top = first.Top;
            Bottom = first.Bottom;
            _parts = new List<Fragment> { first };
        }

        public void Add(Fragment f)
        {
            _parts.Add(f);
            Top = Math.Min(Top, f.Top);
            Bottom = Math.Max(Bottom, f.Bottom);
        }

        // 행을 좌→우로 훑으며 직전 세그먼트 오른쪽 끝과의 간격이 임계값을 넘으면 끊는다.
        public IEnumerable<OcrLine> ToSegments(double gapRatio)
        {
            var sorted = _parts.OrderBy(p => p.Left).ToList();
            var threshold = (Bottom - Top) * gapRatio;

            var seg = new List<Fragment>();
            double segRight = 0;
            foreach (var p in sorted)
            {
                if (seg.Count > 0 && p.Left - segRight > threshold)
                {
                    yield return Build(seg);
                    seg = new List<Fragment>();
                }
                seg.Add(p);
                segRight = seg.Count == 1 ? p.Right : Math.Max(segRight, p.Right);
            }
            if (seg.Count > 0) yield return Build(seg);
        }

        private static OcrLine Build(List<Fragment> seg) => new(
            string.Join(" ", seg.Select(p => p.Text)),
            seg.Min(p => p.Left), seg.Min(p => p.Top), seg.Max(p => p.Right), seg.Max(p => p.Bottom));
    }
}
