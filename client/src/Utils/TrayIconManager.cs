using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace AlctClient.Utils;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? OpenSettingsRequested;
    public event Action<bool>? OverlayToggleRequested;
    public event Action? ExitRequested;

    private bool _overlayVisible;
    private ToolStripMenuItem? _overlayItem;

    public TrayIconManager()
    {
        var menu = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "ALCT",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => OpenSettingsRequested?.Invoke());
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var header = new ToolStripLabel("ALCT") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        var openSettings = new ToolStripMenuItem("설정 열기");
        openSettings.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(
                () => OpenSettingsRequested?.Invoke());
        menu.Items.Add(openSettings);

        _overlayItem = new ToolStripMenuItem("오버레이 메뉴 표시");
        _overlayItem.Click += (_, _) =>
        {
            _overlayVisible = !_overlayVisible;
            _overlayItem.Text = _overlayVisible ? "오버레이 메뉴 숨김" : "오버레이 메뉴 표시";
            System.Windows.Application.Current.Dispatcher.Invoke(
                () => OverlayToggleRequested?.Invoke(_overlayVisible));
        };
        menu.Items.Add(_overlayItem);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("종료");
        exit.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(
                () => ExitRequested?.Invoke());
        menu.Items.Add(exit);

        return menu;
    }

    public void SetOverlayVisible(bool visible)
    {
        _overlayVisible = visible;
        if (_overlayItem != null)
            _overlayItem.Text = visible ? "오버레이 메뉴 숨김" : "오버레이 메뉴 표시";
    }

    public static Icon CreateIcon()
    {
        var path = IcoPath();
        return File.Exists(path) ? new Icon(path) : FallbackIcon();
    }

    private static string IcoPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "alct.ico");

    private static Icon FallbackIcon()
    {
        const int S = 32;
        var bmp = new Bitmap(S, S);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0x8B, 0x7C, 0xF8));
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
