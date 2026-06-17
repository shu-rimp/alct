using AlctClient.Utils;
using AlctClient.Views.Windows;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AlctClient;

public partial class App : Application
{
    private const string MutexName       = "AlctClient-SingleInstance";
    private const string ActivateEvent   = "AlctClient-Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateHandle;
    private CancellationTokenSource? _listenerCts;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ActivateEvent);
                handle.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        _activateHandle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEvent);
        _listenerCts    = new CancellationTokenSource();
        StartActivateListener(_listenerCts.Token);

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x8B, 0x7C, 0xF8));
        new MainWindow().Show();
    }

    private void StartActivateListener(CancellationToken ct)
    {
        Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (_activateHandle!.WaitOne(500))
                    Dispatcher.Invoke(BringSettingsToFront);
            }
        }, ct);
    }

    private static void BringSettingsToFront()
    {
        var settings = Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (settings is null) return;
        if (settings.WindowState == WindowState.Minimized)
            settings.WindowState = WindowState.Normal;
        settings.Show();
        settings.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listenerCts?.Cancel();
        _activateHandle?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("DispatcherUnhandled", e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Logger.Error("AppDomainUnhandled", ex);
    }
}
