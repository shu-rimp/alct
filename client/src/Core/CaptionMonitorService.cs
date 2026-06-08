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
public sealed class CaptionMonitorService : IDisposable
{
    private const int POLL_MS = 200;
    private const int DEBOUNCE_MS = 800;

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
    private int _firedLineCount;          // \n 기준으로 발송 완료된 줄 수
    private string _lastPartialLine = ""; // 현재 진행 중인 미완성 줄
    private DateTime _lastPartialChangeTime = DateTime.MinValue;
    private bool _debounceFired;          // 현재 partial 줄에 대해 디바운스가 이미 발동됐는지

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

        // 새로 완성된 줄 즉시 발송
        for (int i = _firedLineCount; i < completedCount; i++)
            TryFireLine(lines[i].Trim());
        _firedLineCount = completedCount;

        // partial 줄이 바뀌었으면 디바운스 타이머 리셋 + pending 표시 갱신
        if (partialLine != _lastPartialLine)
        {
            _lastPartialLine = partialLine;
            _lastPartialChangeTime = DateTime.UtcNow;
            _debounceFired = false;
            CaptionUpdating?.Invoke(partialLine);
        }
    }

    // 텍스트가 변화 없을 때: 마지막 발화 디바운스 fallback 체크
    private void CheckDebounce()
    {
        if (_debounceFired) return;
        if (string.IsNullOrWhiteSpace(_lastPartialLine)) return;
        if ((DateTime.UtcNow - _lastPartialChangeTime).TotalMilliseconds < DEBOUNCE_MS) return;

        _debounceFired = true;
        TryFireLine(_lastPartialLine);
    }

    // 중복 발송 방지 후 CaptionStabilized 발생
    // DEBOUNCE_MS 이내 동일 문장만 차단 — 반복 발화는 재발송 허용
    private void TryFireLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line.All(c => char.IsPunctuation(c) || char.IsSymbol(c))) return;

        var now = DateTime.UtcNow;
        if (line == _lastFiredLine && (now - _lastFiredTime).TotalMilliseconds < DEBOUNCE_MS) return;

        _lastFiredLine = line;
        _lastFiredTime = now;
        CaptionStabilized?.Invoke(line);
    }

    private void InitLineState(string text)
    {
        var lines = text.Split('\n');
        _firedLineCount = lines.Length - 1;
        _lastPartialLine = lines[^1].Trim();
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
