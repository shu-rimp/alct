using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AlctClient.Utils;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

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

        var exit = new ToolStripMenuItem("종료");
        exit.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(
                () => ExitRequested?.Invoke());
        menu.Items.Add(exit);

        return menu;
    }

    public static Icon CreateIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/alct.ico"));
        return resource is not null ? new Icon(resource.Stream) : FallbackIcon();
    }

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
