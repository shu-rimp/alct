namespace AlctClient.Utils;

// 한국어 조사(allomorph) 보정.
// MyMemory 마스킹 복원 시, MT가 플레이스홀더 토큰에 붙인 조사는 토큰의 가상 발음 기준이라
// 실제 용어의 받침과 어긋날 수 있다(예: "궁가"→"궁이", "궁를"→"궁을"). 이를 용어 받침에 맞게 교정한다.
public static class KoreanParticle
{
    // 조사 쌍 (받침 있을 때, 받침 없을 때). '으로/로'는 ㄹ받침이면 받침이어도 '로'를 쓰는 예외.
    private static readonly (string withBatchim, string withoutBatchim)[] Pairs =
    {
        ("이", "가"),
        ("을", "를"),
        ("은", "는"),
        ("과", "와"),
        ("으로", "로"),
    };

    // 정규식 alternation용 패턴. '으로'가 '로'보다 먼저 와야 greedy하게 매칭됨.
    public const string ParticlePattern = "으로|로|이|가|을|를|은|는|과|와";

    // 마지막 글자에 받침(종성)이 있는지. 한글 음절(가~힣)이 아니면 false.
    private static bool HasBatchim(char c) =>
        c is >= '가' and <= '힣' && (c - 0xAC00) % 28 != 0;

    // 종성이 ㄹ(인덱스 8)인지 — '으로/로' 예외 판정용.
    private static bool IsRieulBatchim(char c) =>
        c is >= '가' and <= '힣' && (c - 0xAC00) % 28 == 8;

    // term 뒤에 올 조사를 term의 받침에 맞게 보정해 반환. 알려진 조사 쌍이 아니면 입력 그대로.
    public static string Correct(string term, string particle)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(particle)) return particle;
        var last = term[^1];

        foreach (var (withB, withoutB) in Pairs)
        {
            if (particle != withB && particle != withoutB) continue;
            if (withB == "으로")  // ㄹ받침은 받침이 있어도 '로'
                return HasBatchim(last) && !IsRieulBatchim(last) ? "으로" : "로";
            return HasBatchim(last) ? withB : withoutB;
        }
        return particle;
    }
}
