using System.Diagnostics;
using System.Windows.Automation;

namespace AlctClient.Core;

public sealed class CaptionMonitorService : IDisposable
{
    private const int MIN_POLL_MS = 200;
    private const int MAX_POLL_MS = 1000;
    private const int POLL_STEP_MS = 100;
    private const int DEBOUNCE_MS = 500;

    private static readonly CacheRequest _nameCache = BuildCacheRequest();

    private static CacheRequest BuildCacheRequest()
    {
        var req = new CacheRequest();
        req.Add(AutomationElement.NameProperty);
        return req;
    }

    private string _lastText = "";
    private string _sentText = "";
    private DateTime _lastChangeTime = DateTime.MinValue;
    private bool _triggered;
    private int _pollInterval = MIN_POLL_MS;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event Action<string>? CaptionStabilized;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _lastText = _sentText = "";
        _triggered = false;
        _pollInterval = MIN_POLL_MS;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _lastText = _sentText = GetLiveCaptionsText() ?? "";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct);
                Poll();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Poll()
    {
        var text = GetLiveCaptionsText();
        if (text is null) return;

        if (text != _lastText)
        {
            _lastText = text;
            _lastChangeTime = DateTime.UtcNow;
            _triggered = false;
            _pollInterval = MIN_POLL_MS;
            return;
        }

        var debounced = !string.IsNullOrWhiteSpace(text) &&
                        (DateTime.UtcNow - _lastChangeTime).TotalMilliseconds >= DEBOUNCE_MS;

        if (!_triggered && debounced)
            FireStabilized(text);

        if (_triggered || debounced)
            _pollInterval = Math.Min(_pollInterval + POLL_STEP_MS, MAX_POLL_MS);
    }

    private void FireStabilized(string text)
    {
        _triggered = true;

        if (_sentText.Length > 0 && !text.StartsWith(_sentText, StringComparison.Ordinal))
        {
            _sentText = text;
            return;
        }

        var newText = text[_sentText.Length..].Trim();
        if (string.IsNullOrWhiteSpace(newText))
        {
            _sentText = text;
            return;
        }

        _sentText = text;
        CaptionStabilized?.Invoke(newText);
    }

    private static string? GetLiveCaptionsText()
    {
        try
        {
            var processes = Process.GetProcessesByName("LiveCaptions");
            if (processes.Length == 0) return null;

            var window = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, processes[0].Id));
            if (window is null) return null;

            using (_nameCache.Activate())
            {
                var elements = window.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

                return string.Join(" ", elements.Cast<AutomationElement>()
                    .Select(el => el.Cached.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            }
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
