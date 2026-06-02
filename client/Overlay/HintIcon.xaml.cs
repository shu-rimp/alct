using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace AlctClient.Overlay;

public partial class HintIcon : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty GuideProperty =
        DependencyProperty.Register(nameof(Guide), typeof(string), typeof(HintIcon),
            new PropertyMetadata(string.Empty, (d, e) =>
                ((HintIcon)d).GuideTextBlock.Text = (string)e.NewValue));

    public static readonly DependencyProperty LinkTextProperty =
        DependencyProperty.Register(nameof(LinkText), typeof(string), typeof(HintIcon),
            new PropertyMetadata(string.Empty, (d, _) => ((HintIcon)d).UpdateLink()));

    public static readonly DependencyProperty LinkUrlProperty =
        DependencyProperty.Register(nameof(LinkUrl), typeof(string), typeof(HintIcon),
            new PropertyMetadata(string.Empty, (d, _) => ((HintIcon)d).UpdateLink()));

    public string Guide
    {
        get => (string)GetValue(GuideProperty);
        set => SetValue(GuideProperty, value);
    }

    public string LinkText
    {
        get => (string)GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    public string LinkUrl
    {
        get => (string)GetValue(LinkUrlProperty);
        set => SetValue(LinkUrlProperty, value);
    }

    private DispatcherTimer? _closeTimer;

    public HintIcon() => InitializeComponent();

    private void UpdateLink()
    {
        var hasLink = !string.IsNullOrEmpty(LinkText) && !string.IsNullOrEmpty(LinkUrl);
        if (hasLink)
        {
            LinkTextRun.Text = LinkText;
            GuideHyperlink.NavigateUri = new Uri(LinkUrl);
        }
        LinkRow.Visibility = hasLink ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _closeTimer?.Stop();
        HintPopup.IsOpen = true;
    }

    private void OnIconMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _closeTimer.Tick += (s, _) =>
        {
            _closeTimer.Stop();
            if (!HintPopup.IsMouseOver)
                HintPopup.IsOpen = false;
        };
        _closeTimer.Start();
    }

    private void OnPopupMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => HintPopup.IsOpen = false;

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
