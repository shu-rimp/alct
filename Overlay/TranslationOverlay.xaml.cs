using AlctClient.Utils;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace AlctClient.Overlay;

public record TranslationEntry(string Translated, string Original);

public partial class TranslationOverlay : Window
{
    private const int MAX_ENTRIES = 5;
    private const int AUTO_HIDE_DELAY_MS = 5000;
    private static readonly System.Windows.Media.Color BgColor = System.Windows.Media.Color.FromRgb(0x16, 0x14, 0x1F);

    private readonly ObservableCollection<TranslationEntry> _entries = new();
    private CancellationTokenSource? _hideTimer;
    private double _opacity = 0.7;

    public TranslationOverlay()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _entries;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsApiHelper.EnableClickThrough(this);
        SnapToDefaultPosition();
        ApplyOpacity();
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.1, 1.0);
        if (IsLoaded) ApplyOpacity();
    }

    private void ApplyOpacity() =>
        RootBorder.Background = new SolidColorBrush(BgColor) { Opacity = _opacity };

    // 드래그로 위치 변경 시 호출 (추후 구현)
    public void SetPosition(double left, double top)
    {
        Left = left;
        Top = top;
        UpdateMaxHeight();
    }

    private void SnapToDefaultPosition()
    {
        Left = SystemParameters.PrimaryScreenWidth - Width - 20;
        Top = 80;
        UpdateMaxHeight();
    }

    private void UpdateMaxHeight() =>
        MaxHeight = SystemParameters.PrimaryScreenHeight - Top - 40;

    public void ShowTranslation(string translated, string original)
    {
        Dispatcher.Invoke(() =>
        {
            _entries.Clear();

            var translatedLines = translated.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var originalLines   = original.Split('\n',   StringSplitOptions.RemoveEmptyEntries);
            var count = Math.Max(translatedLines.Length, originalLines.Length);

            for (int i = 0; i < count && i < MAX_ENTRIES; i++)
            {
                var t = i < translatedLines.Length ? translatedLines[i].Trim() : string.Empty;
                var o = i < originalLines.Length   ? originalLines[i].Trim()   : string.Empty;
                if (!string.IsNullOrWhiteSpace(t) || !string.IsNullOrWhiteSpace(o))
                    _entries.Add(new TranslationEntry(t, o));
            }

            Show();
            ScheduleAutoHide();
        });
    }

    public void ShowAtLiveCaptions(string translated, string original)
    {
        Dispatcher.Invoke(() =>
        {
            _entries.Clear();
            _entries.Add(new TranslationEntry(translated, original));
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
            _ => Dispatcher.Invoke(() => { Hide(); _entries.Clear(); }),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}
