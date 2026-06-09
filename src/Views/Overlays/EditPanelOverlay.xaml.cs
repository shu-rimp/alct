using AlctClient.Utils;
using System.Windows;
using System.Windows.Interop;

namespace AlctClient.Views.Overlays;

public partial class EditPanelOverlay : Window
{
    public event Action? SaveRequested;
    public event Action? CancelRequested;
    public event Action<double>? OpacityChanged;

    public EditPanelOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SnapToPosition();
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(NoActivateHook);
    }

    private static IntPtr NoActivateHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) { handled = true; return (IntPtr)3; } // MA_NOACTIVATE
        return IntPtr.Zero;
    }

    private void SnapToPosition()
    {
        Left = (SystemParameters.PrimaryScreenWidth  - ActualWidth)  / 2;
        Top  =  SystemParameters.PrimaryScreenHeight - ActualHeight - 60;
    }

    public void MoveToMonitor(System.Windows.Forms.Screen screen)
    {
        Left = screen.Bounds.Left + (screen.Bounds.Width  - ActualWidth)  / 2;
        Top  = screen.Bounds.Bottom - ActualHeight - 60;
    }

    public void SetOpacity(double opacity)
    {
        var pct = (int)Math.Round(opacity * 100);
        OpacitySlider.Value   = Math.Clamp(pct, 10, 100);
        OpacityValueText.Text = $"{pct}%";
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is null) return;
        var pct = (int)Math.Round(e.NewValue);
        OpacityValueText.Text = $"{pct}%";
        OpacityChanged?.Invoke(pct / 100.0);
    }

    private void OnSave(object sender, RoutedEventArgs e)   => SaveRequested?.Invoke();
    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
