using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AlctClient.Views.Overlays;

// 캡처 핫키를 길게 눌렀을 때 뜨는 1회성 드래그 영역 선택 오버레이.
// 사용자가 마우스로 사각형을 그리면 그 영역(저장된 캡처 영역과 무관)으로 한 번만 번역한다.
// 다른 오버레이와 달리 클릭/키 입력을 받아야 하므로 클릭 통과를 쓰지 않는다.
public partial class DragSelectOverlay : Window
{
    private const double MIN_SIZE = 5;  // 이보다 작으면 오선택으로 보고 취소

    private WpfPoint _start;
    private bool _dragging;

    // 선택 완료 — 화면 절대 좌표 사각형. 취소 시 Cancelled.
    public event Action<Rectangle>? SelectionCompleted;
    public event Action? Cancelled;

    public DragSelectOverlay()
    {
        InitializeComponent();
    }

    // 지정한 모니터 전체를 덮어 표시. CaptureRegionOverlay와 동일하게 화면 좌표를 그대로 사용.
    public void ShowForScreen(System.Windows.Forms.Screen screen)
    {
        Left   = screen.Bounds.Left;
        Top    = screen.Bounds.Top;
        Width  = screen.Bounds.Width;
        Height = screen.Bounds.Height;

        SelectionRect.Visibility = Visibility.Collapsed;
        HintBox.Visibility = Visibility.Visible; 
        _dragging = false;

        Show();
        Activate();
        Focus();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        HintBox.Visibility = Visibility.Collapsed;
        UpdateRect(_start);
        SelectionRect.Visibility = Visibility.Visible;
        RootCanvas.CaptureMouse();
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateRect(e.GetPosition(RootCanvas));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        RootCanvas.ReleaseMouseCapture();

        double w = SelectionRect.Width, h = SelectionRect.Height;
        if (w < MIN_SIZE || h < MIN_SIZE) { Cancel(); return; }

        // 캔버스 내 좌표 + 창 위치 = 화면 절대 좌표(CaptureRegionOverlay와 동일 좌표계)
        var region = new Rectangle(
            (int)(Left + Canvas.GetLeft(SelectionRect)),
            (int)(Top  + Canvas.GetTop(SelectionRect)),
            (int)w, (int)h);

        Hide();
        SelectionCompleted?.Invoke(region);
    }

    private void OnCancel(object sender, MouseButtonEventArgs e) => Cancel();

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
        base.OnKeyDown(e);
    }

    private void Cancel()
    {
        _dragging = false;
        if (RootCanvas.IsMouseCaptured) RootCanvas.ReleaseMouseCapture();
        Hide();
        Cancelled?.Invoke();
    }

    private void UpdateRect(WpfPoint current)
    {
        double x = Math.Min(_start.X, current.X);
        double y = Math.Min(_start.Y, current.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width  = Math.Abs(current.X - _start.X);
        SelectionRect.Height = Math.Abs(current.Y - _start.Y);
    }
}
