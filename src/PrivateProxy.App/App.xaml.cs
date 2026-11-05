using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PrivateProxy.Core;

namespace PrivateProxy.App;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // A second copy would fight over winws and WinDivert, so only one may live.
        _mutex = new Mutex(false, @"Local\PrivateProxy.SingleInstance");
        var acquired = false;
        for (var attempt = 0; attempt < 30 && !acquired; attempt++)
        {
            try
            {
                acquired = _mutex.WaitOne(200);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
        }

        if (!acquired)
        {
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();
        Log.Info("Старт приложения");

        var args = e.Args ?? [];
        bool Has(string value) => args.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

        var start = Has("--start");
        var test = Has("--test");
        var minimized = Has("--autostart");

        var window = new MainWindow(start, test, minimized);
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log only: a modal dialog here would spam and block the UI thread when
        // an error repeats (as a layout/measure exception can).
        Log.Error("Необработанная ошибка: " + e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // not the owner
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }
}
