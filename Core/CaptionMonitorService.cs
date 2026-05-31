using System.Diagnostics;
using System.Windows.Automation;

namespace AlctClient.Core;

public sealed class CaptionMonitorService : IDisposable
{
    private const int POLL_INTERVAL_MS = 100;
    private const int DEBOUNCE_MS = 1000;

    private string _lastText = "";
    private string _sentText = "";
    private DateTime _lastChangeTime = DateTime.MinValue;
    private bool _triggered;
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
        _lastText = "";
        _sentText = "";
        _triggered = false;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var initial = GetLiveCaptionsText() ?? "";
        _lastText = initial;
        _sentText = initial;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(POLL_INTERVAL_MS, ct);
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
        }
        else if (!_triggered && !string.IsNullOrWhiteSpace(text) &&
                 (DateTime.UtcNow - _lastChangeTime).TotalMilliseconds >= DEBOUNCE_MS)
        {
            _triggered = true;

            bool prefixMatch = _sentText.Length > 0 && text.StartsWith(_sentText, StringComparison.Ordinal);

            if (!prefixMatch && _sentText.Length > 0)
            {
                _sentText = text;
                return;
            }

            var textToSend = prefixMatch ? text[_sentText.Length..].Trim() : text;

            if (string.IsNullOrWhiteSpace(textToSend))
            {
                _sentText = text;
                return;
            }

            _sentText = text;
            CaptionStabilized?.Invoke(textToSend);
        }
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

            var textElements = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

            var parts = new List<string>();
            foreach (AutomationElement el in textElements)
            {
                var name = el.Current.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    parts.Add(name);
            }

            return string.Join(" ", parts);
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
