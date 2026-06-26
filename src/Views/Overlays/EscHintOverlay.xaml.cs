using AlctClient.Utils;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AlctClient.Views.Overlays;

// "ESC를 눌러 번역 숨기기" 안내 전용 작은 창. 캡처 영역 창과 분리되어 있어
// 화면 좌측 하단 고정 위치에 떠도 번역 박스를 가리지 않는다. 클릭 통과.
public partial class EscHintOverlay : Window
{
    private const double MARGIN = 12;  // 화면 가장자리에서의 여백(DIU)

    public EscHintOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowsApiHelper.EnableClickThrough(this);
    }

    // 캡처 영역이 속한 화면의 좌측 하단에 표시.
    public void ShowAtScreen(System.Drawing.Rectangle captureRegion)
    {
        var screen = System.Windows.Forms.Screen.FromRectangle(captureRegion);
        Show();
        UpdateLayout();  // ActualHeight/Width 확정(SizeToContent)
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = screen.Bounds.Left   / dpi.DpiScaleX + MARGIN;
        Top  = screen.Bounds.Bottom / dpi.DpiScaleY - ActualHeight - MARGIN;
    }
}
