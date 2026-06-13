using System.Diagnostics;
using System.Windows.Automation;

namespace AlctClient.Core;

// Live Captions 텍스트 누적 방식:
//   "Hello world\nGo mid\nBuy item"
//    ^^^^^^^^^^^  ^^^^^^   ^^^^^^^
//    완성 줄(\n)   완성 줄   진행 중(partial)
// 1. STT가 완료된 문장들이 개행문자(\n)로 들어온다.
//    여기서 문장이란 실제 의미가 아닌 텍스트 덩어리를 의미함.
//    (화자분리와 문장 끝맺음 등이 정확하지 않음)
// 2. 진행중(partial)일 때는 단어가 바뀌거나 추가됨
// 3. 마지막 발화에 대해서는 개행문자(\n) 처리를 하지 않음.
//
// 탐지 전략:
//   1차: \n 추가 시 완성 줄 즉시 발송
//   2차: partial 줄이 DEBOUNCE_MS 동안 변화 없으면 발송
//       (마지막 발화 fallback — Live Captions가 마지막 발화에 \n을 붙이지 않는 경우 대비)
//   3차: partial이 계속 자라기만 함 → stable prefix를 경계 기준으로 부분 커밋 (연속 발화 케이스)
public sealed class CaptionMonitorService : IDisposable
{
    private const int POLL_MS = 25;
    private const int DEBOUNCE_MS = 800;       // 발화 중간 숨 고르기(~0.8초)에 조각이 발사되지 않도록 여유 있게
    private const int MIN_COMMIT_LENGTH = 100;  // uncommitted가 이 가중 길이(CJK=2) 이상 쌓이면 강제 커밋 고려 — CJK ~50자, 라틴 ~100자
    private const int PREFIX_STABLE_COUNT = 3;  // 앞부분이 이 횟수만큼 연속 안정이면 확정으로 간주
    private const int MAX_PARTIAL_MS = 6000;    // 이 시간 초과 시 무조건 flush
    private const int FIRE_DEDUP_MS = 150;      // 청크 커밋 직후 \n 완성 등으로 같은 줄이 즉시 재발사되는 기계적 중복만 차단

    private static readonly CacheRequest _nameCache = BuildCacheRequest();
    private static readonly PropertyCondition _captionsTextBlockCondition =
        new(AutomationElement.AutomationIdProperty, "CaptionsTextBlock");

    private static CacheRequest BuildCacheRequest()
    {
        var req = new CacheRequest();
        req.Add(AutomationElement.NameProperty);
        return req;
    }

    // --- 폴링 상태 ---
    private string _lastText = "";

    // --- 줄 처리 상태 ---
    private int _firedLineCount;
    private string _lastPartialLine = "";
    private DateTime _lastPartialChangeTime = DateTime.MinValue;
    private bool _debounceFired;

    // --- stable prefix 커밋 상태 ---
    private int _committedOffset;          // 현재 partial 중 이미 번역 발송된 앞부분의 문자 수
    private string _lastRemaining = "";    // 직전 poll의 uncommitted remaining
    private int _stableCount;             // 앞부분이 변화 없이 뒤만 늘어난 연속 횟수
    private DateTime _partialStartTime = DateTime.MinValue;

    // --- 중복 발송 방지 ---
    private string _lastFiredLine = "";
    private DateTime _lastFiredTime = DateTime.MinValue;

    private CancellationTokenSource? _cts;
    private bool _disposed;
    private AutomationElement? _captionsWindow;

    public event Action<string>? CaptionUpdating;
    public event Action<string>? CaptionStabilized;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _lastText = _lastPartialLine = _lastFiredLine = "";
        _lastFiredTime = DateTime.MinValue;
        _firedLineCount = 0;
        _debounceFired = false;
        ResetStableState();
        _captionsWindow = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // 유효한 텍스트를 얻을 때까지 대기 — 빈 상태로 시작하면 기존 누적된 문장이 한번에 재번역됨
        string? initial = null;
        while (!ct.IsCancellationRequested && initial is null)
        {
            initial = GetLiveCaptionsText();
            if (initial is null)
            {
                try { await Task.Delay(POLL_MS, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        _lastText = initial ?? "";
        InitLineState(_lastText);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(POLL_MS, ct);
                Poll();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Poll()
    {
        var text = GetLiveCaptionsText();
        if (text is null) return;

        if (text == _lastText) { CheckDebounce(); return; }

        _lastText = text;
        ProcessTextChange(text);
    }

    // 텍스트가 변화했을 때: 완성 줄 발송 + partial 줄 갱신
    private void ProcessTextChange(string text)
    {
        var lines = text.Split('\n');
        var completedCount = lines.Length - 1; // 마지막 요소는 현재 진행 중인 partial
        var partialLine = lines[^1].Trim();

        // Live Captions가 초기화돼 줄 수가 줄었을 경우 — 현재 위치로 맞춤
        if (completedCount < _firedLineCount)
            _firedLineCount = completedCount;

        bool hasNewLines = completedCount > _firedLineCount;

        // 새로 완성된 줄 즉시 발송
        // 첫 번째 줄은 이전 partial이었으므로 committed offset 이후 분만 발송
        for (int i = _firedLineCount; i < completedCount; i++)
        {
            var line = lines[i].Trim();
            if (i == _firedLineCount)
                line = StripCommittedLine(line);
            TryFireLine(line);
        }
        _firedLineCount = completedCount;

        if (hasNewLines)
            ResetStableState();

        // partial 변경 또는 완성 줄 소화 시 pending 갱신
        // hasNewLines 케이스: \n 소화 즉시 이전 발화 꼬리를 pending에서 제거
        if (partialLine != _lastPartialLine || hasNewLines)
        {
            bool partialChanged = partialLine != _lastPartialLine;
            _lastPartialLine = partialLine;
            if (partialChanged)
            {
                _lastPartialChangeTime = DateTime.UtcNow;
                _debounceFired = false;
                CheckStablePrefix(partialLine); // _committedOffset 갱신될 수 있음
            }
            // 커밋된 앞부분을 제외한 remaining만 pending에 표시 (전부 커밋이면 빈 문자열)
            var pending = _committedOffset >= partialLine.Length
                ? ""
                : partialLine[_committedOffset..].TrimStart();
            CaptionUpdating?.Invoke(pending);
        }
    }

    // 텍스트가 변화 없을 때: 마지막 발화 디바운스 fallback 체크
    private void CheckDebounce()
    {
        if (_debounceFired) return;
        if (string.IsNullOrWhiteSpace(_lastPartialLine)) return;
        if ((DateTime.UtcNow - _lastPartialChangeTime).TotalMilliseconds < DEBOUNCE_MS) return;

        _debounceFired = true;
        TryFireLine(StripCommittedLine(_lastPartialLine));

        // 줄은 아직 살아있음 — 발화 재개 시 같은 줄에 이어붙을 수 있으므로
        // 오프셋을 리셋하지 않고 현재까지 전체를 커밋 처리 (앞부분 재발송 방지)
        _committedOffset = _lastPartialLine.Length;
        _lastRemaining = "";
        _stableCount = 0;
        _partialStartTime = DateTime.UtcNow;

        // 발사된 내용이 pending에 남아있지 않도록 비움
        CaptionUpdating?.Invoke("");
    }

    // partial이 계속 자라기만 할 때: stable prefix를 경계 기준으로 부분 커밋
    private void CheckStablePrefix(string partialLine)
    {
        // STT 후행 수정으로 줄이 커밋 지점보다 짧아진 경우 — 리셋하면
        // 이미 번역된 앞부분 전체가 재발송되므로 클램프로 처리
        if (_committedOffset > partialLine.Length)
            _committedOffset = partialLine.Length;

        var remaining = partialLine[_committedOffset..];

        // 앞부분 유지된 채 뒤만 늘어났는지 확인
        var common = CommonPrefixLength(_lastRemaining, remaining);
        bool onlyGrew = common >= _lastRemaining.Length && remaining.Length > _lastRemaining.Length;
        _stableCount = onlyGrew ? _stableCount + 1 : 0;
        _lastRemaining = remaining;

        bool shouldCommit =
            EffectiveLength(remaining) >= MIN_COMMIT_LENGTH &&
            (_stableCount >= PREFIX_STABLE_COUNT ||
             (DateTime.UtcNow - _partialStartTime).TotalMilliseconds >= MAX_PARTIAL_MS);

        if (!shouldCommit) return;

        // 끝 10자는 아직 흔들릴 수 있으니 제외하고 안전한 경계 탐색
        var cut = FindLastBoundary(remaining, Math.Max(0, remaining.Length - 10));
        if (cut <= 0) return;

        // 약한 경계(쉼표/공백) 컷은 너무 짧은 조각을 만들면 보류 — 문장 종결부호 컷은 항상 허용
        if (!IsSentenceEnd(remaining[cut - 1]) &&
            EffectiveLength(remaining[..cut]) < MIN_COMMIT_LENGTH / 2)
            return;

        var chunk = remaining[..cut].Trim();
        if (string.IsNullOrWhiteSpace(chunk)) return;

        TryFireLine(chunk);
        _committedOffset += cut;
        _lastRemaining = remaining[cut..];
        _stableCount = 0;
        _partialStartTime = DateTime.UtcNow;
    }

    // 완성 줄에서 이미 커밋된 앞부분을 제거. 전부 커밋됐으면 빈 문자열
    private string StripCommittedLine(string line)
    {
        if (_committedOffset <= 0) return line;
        if (_committedOffset >= line.Length) return "";
        return line[_committedOffset..].TrimStart();
    }

    private void ResetStableState()
    {
        _committedOffset = 0;
        _lastRemaining = "";
        _stableCount = 0;
        _partialStartTime = DateTime.UtcNow;
    }

    // 중복 발송 방지 후 CaptionStabilized 발생
    // FIRE_DEDUP_MS(짧은 윈도우) 이내 동일 문장만 차단 — 기계적 재발사만 막고,
    // 발화 내 의도적 단어 반복("가자 가자")은 정상 입력이므로 재발송 허용
    private void TryFireLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line.All(c => char.IsPunctuation(c) || char.IsSymbol(c))) return;

        var now = DateTime.UtcNow;
        if (line == _lastFiredLine && (now - _lastFiredTime).TotalMilliseconds < FIRE_DEDUP_MS) return;

        _lastFiredLine = line;
        _lastFiredTime = now;
        CaptionStabilized?.Invoke(line);
    }

    private void InitLineState(string text)
    {
        var lines = text.Split('\n');
        _firedLineCount = lines.Length - 1;
        _lastPartialLine = lines[^1].Trim();
        ResetStableState();
    }

    // text 내에서 maxPos 이전의 마지막 안전한 잘라내기 위치 반환
    // 우선순위: 문장 종결부호 > 절 구분(쉼표류) > 공백(단어 경계)
    // CJK(중/일)는 공백이 없으므로 전각 부호가 유일한 경계
    private static int FindLastBoundary(string text, int maxPos)
    {
        maxPos = Math.Min(maxPos, text.Length);

        for (int i = maxPos - 1; i >= 0; i--)
        {
            if (IsSentenceEnd(text[i]))
                return i + 1;
        }

        for (int i = maxPos - 1; i >= 0; i--)
        {
            if (IsClauseBreak(text[i]))
                return i + 1;
        }

        for (int i = maxPos - 1; i >= 0; i--)
        {
            if (text[i] == ' ')
                return i + 1;
        }

        return 0;
    }

    private static bool IsSentenceEnd(char c) =>
        c is '.' or '?' or '!' or '。' or '？' or '！' or '．';

    private static bool IsClauseBreak(char c) =>
        c is ',' or '、' or '，';

    // CJK는 문자당 정보량이 라틴의 ~2배 — 길이 기준을 언어 불문 일관되게 적용하기 위한 가중 길이
    private static int EffectiveLength(string text)
    {
        int len = 0;
        foreach (var c in text)
            len += IsCjk(c) ? 2 : 1;
        return len;
    }

    private static bool IsCjk(char c) =>
        (c >= 0x3040 && c <= 0x30FF) ||  // 히라가나·가타카나
        (c >= 0x3400 && c <= 0x9FFF) ||  // CJK 한자
        (c >= 0xAC00 && c <= 0xD7AF) ||  // 한글
        (c >= 0xFF00 && c <= 0xFF60);    // 전각 영숫자·부호

    private static int CommonPrefixLength(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return len;
    }

    private string? GetLiveCaptionsText()
    {
        try
        {
            _captionsWindow ??= FindLiveCaptionsWindow();
            if (_captionsWindow is null) return null;

            using (_nameCache.Activate())
            {
                var el = _captionsWindow.FindFirst(TreeScope.Descendants, _captionsTextBlockCondition);
                var name = el?.Cached.Name;
                return string.IsNullOrEmpty(name) ? null : name;
            }
        }
        catch
        {
            _captionsWindow = null;
            return null;
        }
    }

    private static AutomationElement? FindLiveCaptionsWindow()
    {
        var processes = Process.GetProcessesByName("LiveCaptions");
        if (processes.Length == 0) return null;
        var pid = processes[0].Id;
        foreach (var p in processes) p.Dispose();
        return AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
