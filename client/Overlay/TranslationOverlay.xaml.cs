using AlctClient.Utils;
using System.Windows;

namespace AlctClient.Overlay;

public partial class TranslationOverlay : Window
{
    private const int AUTO_HIDE_DELAY_MS = 5000;

    private CancellationTokenSource? _hideTimer;

    public TranslationOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowsApiHelper.EnableClickThrough(this);
    }

    public void ShowTranslation(string text)
    {
        Dispatcher.Invoke(() =>
        {
            TranslationText.Text = text;
            Show();
            ScheduleAutoHide();
        });
    }

    private void ScheduleAutoHide()
    {
        _hideTimer?.Cancel();
        _hideTimer = new CancellationTokenSource();
        var token = _hideTimer.Token;

        Task.Delay(AUTO_HIDE_DELAY_MS, token).ContinueWith(
            _ => Dispatcher.Invoke(Hide),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}
