using AlctClient.Core;
using AlctClient.Utils;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace AlctClient.Views.Modals;

public partial class UpdateModal : Window
{
    private readonly string _downloadUrl;

    internal UpdateModal(UpdateInfo info)
    {
        InitializeComponent();
        _downloadUrl = info.DownloadUrl;

        var asm = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionText.Text = asm is not null
            ? $"{asm.Major}.{asm.Minor}.{Math.Max(asm.Build, 0)}"
            : "알 수 없음";
        LatestVersionText.Text = info.LatestVersion;
        ReleaseNotesText.Text  = info.ReleaseNotes;
    }

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("UpdateDownload", ex); }
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
