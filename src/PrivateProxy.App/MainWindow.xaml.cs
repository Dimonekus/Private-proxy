using System.ComponentModel;
using System.Windows;
using PrivateProxy.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PrivateProxy.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _trayToggle;
    private readonly bool _startRequested;
    private readonly bool _testRequested;
    private readonly bool _startMinimized;
    private bool _allowClose;
    private bool _scrollPending;

    public MainWindow(bool startRequested, bool testRequested, bool startMinimized)
    {
        _startRequested = startRequested;
        _testRequested = testRequested;
        _startMinimized = startMinimized;

        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.ElevationRequested += OnElevationRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Logs.CollectionChanged += (_, _) => QueueScrollLogToEnd();

        _trayToggle = new Forms.ToolStripMenuItem("Запустить");
        _trayToggle.Click += async (_, _) => await ToggleEngineAsync();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApplication());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Private Proxy",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.Click += (_, e) =>
        {
            if (e is Forms.MouseEventArgs { Button: Forms.MouseButtons.Left })
            {
                ShowFromTray();
            }
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        await _viewModel.InitializeAsync();
        UpdateTray();

        if (_startMinimized)
        {
            Hide();
        }

        if (_startRequested)
        {
            await _viewModel.StartSelectedAsync(allowElevation: false);
        }
        else if (_viewModel.ShouldAutoStartEngine && _viewModel.IsAdmin)
        {
            // Restore the previous working mode after a reboot, or honour the
            // explicit "start on launch" setting.
            await _viewModel.StartSelectedAsync(allowElevation: false);
        }

        if (_testRequested)
        {
            await _viewModel.TestAllAsync(allowElevation: false);
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await _viewModel.StartSelectedAsync();

    private async void Stop_Click(object sender, RoutedEventArgs e) => await _viewModel.StopAsync();

    private async void Test_Click(object sender, RoutedEventArgs e) => await _viewModel.TestAllAsync();

    private void RefreshEngine_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshEngine();

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        var error = _viewModel.ToggleAutostart();
        if (error is not null)
        {
            MessageBox.Show(this, error, "Private Proxy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLists_Click(object sender, RoutedEventArgs e) => MainViewModel.OpenFolder(AppPaths.ListsDir);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => MainViewModel.OpenFolder(AppPaths.LogsDir);

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _viewModel.Logs.Clear();

    private void OnElevationRequested(string arguments)
    {
        _viewModel.SaveSettings();

        if (!Core.Services.Elevation.RelaunchElevated(arguments))
        {
            MessageBox.Show(this,
                "Без прав администратора winws не сможет перехватывать трафик.",
                "Private Proxy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExitApplication();
    }

    private async Task ToggleEngineAsync()
    {
        if (_viewModel.IsRunning)
        {
            await _viewModel.StopAsync();
        }
        else
        {
            await _viewModel.StartSelectedAsync();
        }

        UpdateTray();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.StatusText)
            or nameof(MainViewModel.IsRunning)
            or nameof(MainViewModel.StatusDetail))
        {
            UpdateTray();
        }
    }

    private void UpdateTray()
    {
        var text = $"Private Proxy — {_viewModel.StatusText}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        _trayToggle.Text = _viewModel.IsRunning ? "Остановить" : "Запустить";
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ScrollLogToEnd()
    {
        try
        {
            if (!IsLoaded || LogList.Items.Count == 0)
            {
                return;
            }

            var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(LogList);
            scrollViewer?.ScrollToEnd();
        }
        catch
        {
            // Auto-scroll must never take the app down, whatever the layout state is.
        }
    }

    private void QueueScrollLogToEnd()
    {
        if (_scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _scrollPending = false;
            ScrollLogToEnd();
        }));
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _tray.Visible = false;
        _tray.Dispose();
        _viewModel.Shutdown();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        var wasClosingForExit = _allowClose;
        _allowClose = true;
        _tray.Visible = false;
        _tray.Dispose();
        _viewModel.Shutdown();
        base.OnClosing(e);

        if (!wasClosingForExit)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // fall through to the generic icon
        }

        return Drawing.SystemIcons.Application;
    }
}
