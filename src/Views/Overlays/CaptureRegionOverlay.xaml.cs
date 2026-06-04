using AlctClient.Utils;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;

namespace AlctClient.Views.Overlays;

public partial class CaptureRegionOverlay : Window
{
    private const double MAX_WIDTH_RATIO  = 0.50;
    private const double MAX_HEIGHT_RATIO = 0.30;

    public event Action<Rectangle>? SaveRequested;
    public event Action? CancelRequested;

    public CaptureRegionOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowsApiHelper.ExcludeFromCapture(this);
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
                ?.AddHook(ResizeHook);
    }

    public void LoadRegion(Rectangle region)
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        MaxWidth  = screen.Width  * MAX_WIDTH_RATIO;
        MaxHeight = screen.Height * MAX_HEIGHT_RATIO;

        Left   = region.X;
        Top    = region.Y;
        Width  = region.Width;
        Height = region.Height;
        UpdateSizeLabel();
    }

    private void UpdateSizeLabel()
        => SizeLabel.Text = $"{(int)Width} × {(int)Height}";

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateSizeLabel();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var region = new Rectangle((int)Left, (int)Top, (int)Width, (int)Height);
        Hide();
        SaveRequested?.Invoke(region);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Hide();
        CancelRequested?.Invoke();
    }

    private IntPtr ResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0084) return IntPtr.Zero; // WM_NCHITTEST

        var pos = PointFromScreen(new System.Windows.Point(
            (short)(lParam.ToInt32() & 0xFFFF),
            (short)((lParam.ToInt32() >> 16) & 0xFFFF)));

        const int B = 8;
        double w = ActualWidth, h = ActualHeight;
        bool left = pos.X < B,  right = pos.X > w - B;
        bool top  = pos.Y < B,  bottom = pos.Y > h - B;

        if (top    && left)  { handled = true; return (IntPtr)13; } // HTTOPLEFT
        if (top    && right) { handled = true; return (IntPtr)14; } // HTTOPRIGHT
        if (bottom && left)  { handled = true; return (IntPtr)16; } // HTBOTTOMLEFT
        if (bottom && right) { handled = true; return (IntPtr)17; } // HTBOTTOMRIGHT
        if (left)            { handled = true; return (IntPtr)10; } // HTLEFT
        if (right)           { handled = true; return (IntPtr)11; } // HTRIGHT
        if (top)             { handled = true; return (IntPtr)12; } // HTTOP
        if (bottom)          { handled = true; return (IntPtr)15; } // HTBOTTOM

        return IntPtr.Zero;
    }
}
