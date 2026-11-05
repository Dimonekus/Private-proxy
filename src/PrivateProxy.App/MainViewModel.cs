using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using PrivateProxy.Core;
using PrivateProxy.Core.Models;
using PrivateProxy.Core.Services;

namespace PrivateProxy.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();
    private readonly StrategyCatalogLoader _catalogLoader = new();
    private readonly EngineLocator _engineLocator = new();
    private readonly StrategyTester _tester = new();
    private readonly WinwsRunner _runner = new();
    private readonly SiteManager _siteManager = new();
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _uptimeTimer;

    private AppSettings _settings = new();
    private EngineStatus _engine = new();
    private Strategy? _selectedStrategy;
    private bool _initialized;

    private string _statusText = "Выключено";
    private string _statusDetail = "Обход DPI не активен";
    private Brush _statusBrush = (Brush)System.Windows.Application.Current.Resources["Muted"];
    private string _engineText = "проверяю движок…";
    private string _testStatus = "";
    private bool _isBusy;
    private bool _isRunning;
    private bool _enginePresent;
    private bool _engineVerified;
    private bool _isAdmin;
    private bool _autoRestart = true;
    private bool _startOnLaunch;
    private bool _minimizeToTray = true;
    private bool _autostartEnabled;
    private string _newSite = "";
    private string _siteHint = "";

    public MainViewModel()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            if (_runner.State == RunnerState.Running)
            {
                StatusDetail = $"{_runner.CurrentStrategy?.Name ?? "—"} · {FormatUptime(_runner.Uptime)}";
            }
        };

        _runner.StateChanged += (_, _) => _ui.Post(_ => RefreshStatus(), null);
        Log.LineWritten += line => _ui.Post(_ => AppendLog(line), null);
    }

    public ObservableCollection<Strategy> Strategies { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<StrategyTestResult> TestResults { get; } = [];
    public ObservableCollection<SiteEntry> Sites { get; } = [];

    /// <summary>Raised when an action needs administrator rights.</summary>
    public event Action<string>? ElevationRequested;

    public Strategy? SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            if (!SetField(ref _selectedStrategy, value))
            {
                return;
            }

            if (_initialized && value is not null)
            {
                _settings.SelectedStrategyId = value.Id;
                _settingsStore.Save(_settings);
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetField(ref _statusDetail, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public string EngineText
    {
        get => _engineText;
        private set => SetField(ref _engineText, value);
    }

    public string TestStatus
    {
        get => _testStatus;
        private set => SetField(ref _testStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanTest));
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(PrimaryActionText));
            }
        }
    }

    public bool EnginePresent
    {
        get => _enginePresent;
        private set => SetField(ref _enginePresent, value);
    }

    public bool EngineVerified
    {
        get => _engineVerified;
        private set => SetField(ref _engineVerified, value);
    }

    public bool IsAdmin
    {
        get => _isAdmin;
        private set => SetField(ref _isAdmin, value);
    }

    public bool AutoRestart
    {
        get => _autoRestart;
        set
        {
            if (SetField(ref _autoRestart, value))
            {
                _settings.AutoRestart = value;
                _settingsStore.Save(_settings);
            }
        }
    }

    public bool StartOnLaunch
    {
        get => _startOnLaunch;
        set
        {
            if (SetField(ref _startOnLaunch, value))
            {
                _settings.StartOnLaunch = value;
                _settingsStore.Save(_settings);
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetField(ref _minimizeToTray, value))
            {
                _settings.MinimizeToTray = value;
                _settingsStore.Save(_settings);
            }
        }
    }

    /// <summary>
    /// True when the engine should be started automatically on startup: either
    /// the user asked for it explicitly, or it was running before shutdown.
    /// </summary>
    public bool ShouldAutoStartEngine => _settings.StartOnLaunch || _settings.EngineWasRunning;

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        private set
        {
            if (SetField(ref _autostartEnabled, value))
            {
                OnPropertyChanged(nameof(AutostartButtonText));
            }
        }
    }

    public string AutostartButtonText => AutostartEnabled ? "Убрать из автозапуска" : "Добавить в автозапуск";
    public bool CanStart => !IsBusy && !IsRunning;
    public bool CanTest => !IsBusy;
    public string PrimaryActionText => IsRunning ? "Остановить" : "Запустить";
    public string AdminText => IsAdmin ? "права администратора есть" : "без прав администратора";

    public string NewSite
    {
        get => _newSite;
        set => SetField(ref _newSite, value);
    }

    public string SiteHint
    {
        get => _siteHint;
        private set => SetField(ref _siteHint, value);
    }

    public async Task InitializeAsync()
    {
        AppPaths.EnsureCreated();
        ListBootstrap.EnsureLists();
        ReloadSites();

        _settings = _settingsStore.Load();
        _autoRestart = _settings.AutoRestart;
        _startOnLaunch = _settings.StartOnLaunch;
        _minimizeToTray = _settings.MinimizeToTray;

        Strategies.Clear();
        foreach (var strategy in _catalogLoader.LoadAll())
        {
            Strategies.Add(strategy);
        }

        SelectedStrategy = Strategies.FirstOrDefault(s => s.Id == _settings.SelectedStrategyId)
                           ?? Strategies.FirstOrDefault(s => s.Recommended)
                           ?? Strategies.FirstOrDefault();

        IsAdmin = Elevation.IsAdministrator;
        AutostartEnabled = AutostartService.IsEnabled();
        RefreshEngine();
        Log.Info($"Движок: {_engine.Summary}");

        foreach (var line in Log.Recent)
        {
            AppendLog(line);
        }

        _initialized = true;
        RefreshStatus();
        _uptimeTimer.Start();

        Log.Info($"Приложение запущено. Стратегий: {Strategies.Count}. {AdminText}.");

        await Task.CompletedTask;
    }

    public void RefreshEngine()
    {
        _engine = _engineLocator.Inspect();
        EnginePresent = _engine.Present;
        EngineVerified = _engine.Verified;
        EngineText = _engine.Summary;
    }

    public async Task StartSelectedAsync(bool allowElevation = true)
    {
        if (IsBusy || IsRunning || SelectedStrategy is null)
        {
            return;
        }

        if (!IsAdmin)
        {
            if (allowElevation && ElevationRequested is not null)
            {
                ElevationRequested("--start");
            }
            else
            {
                StatusText = "Нужны права администратора";
                StatusDetail = "Запустите приложение от имени администратора.";
            }

            return;
        }

        if (!EnginePresent)
        {
            StatusText = "Движок не установлен";
            StatusDetail = "Выполните scripts/fetch-engine.ps1";
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await _runner.StartAsync(SelectedStrategy, AppPaths.EngineDir, AutoRestart);
            if (!outcome.Ok)
            {
                StatusText = "Не удалось запустить";
                StatusDetail = outcome.Error ?? "неизвестная ошибка";
                StatusBrush = (Brush)System.Windows.Application.Current.Resources["Red"];
            }
            else
            {
                _settings.EngineWasRunning = true;
                _settingsStore.Save(_settings);
            }
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
        }
    }

    public async Task StopAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _runner.StopAsync();
            _settings.EngineWasRunning = false;
            _settingsStore.Save(_settings);
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
        }
    }

    public async Task TestAllAsync(bool allowElevation = true)
    {
        if (IsBusy)
        {
            return;
        }

        if (!IsAdmin)
        {
            if (allowElevation && ElevationRequested is not null)
            {
                ElevationRequested("--test");
            }

            return;
        }

        if (Strategies.Count == 0)
        {
            return;
        }

        await _runner.StopAsync();
        RefreshStatus();

        IsBusy = true;
        TestResults.Clear();
        TestStatus = "Подготовка…";

        try
        {
            var progress = new Progress<TestProgress>(p =>
            {
                TestStatus = $"{p.Index}/{p.Total} · {p.StrategyName}: {p.Phase}";
            });

            var results = await _tester.TestAllAsync(Strategies.ToList(), progress);
            foreach (var result in results.OrderByDescending(r => r.Succeeded).ThenBy(r => r.AverageLatencyMs))
            {
                TestResults.Add(result);
            }

            var best = results.Where(r => r.IsSuccess).OrderBy(r => r.AverageLatencyMs).FirstOrDefault()
                       ?? results.OrderByDescending(r => r.Succeeded).FirstOrDefault();

            TestStatus = best is null
                ? "Тест не дал результатов"
                : $"Лучшая: {best.StrategyName} · {best.Summary}";

            if (best is { IsSuccess: true })
            {
                SelectedStrategy = Strategies.FirstOrDefault(s => s.Id == best.StrategyId) ?? SelectedStrategy;
            }
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
        }
    }

    public string? ToggleAutostart()
    {
        if (!IsAdmin)
        {
            return "Для автозапуска нужны права администратора.";
        }

        var result = AutostartEnabled ? AutostartService.Disable() : AutostartService.Enable();
        if (!result.Ok)
        {
            return string.IsNullOrWhiteSpace(result.Output)
                ? "Не удалось изменить автозапуск."
                : result.Output;
        }

        AutostartEnabled = !AutostartEnabled;
        return null;
    }

    public void ReloadSites()
    {
        Sites.Clear();
        foreach (var site in _siteManager.Load())
        {
            Sites.Add(site);
        }
    }

    public async Task ApplySiteToggleAsync(SiteEntry site)
    {
        _siteManager.SetEnabled(site.Domain, site.Enabled);
        ReloadSites();
        Log.Info($"Сайт {(site.Enabled ? "включён" : "выключен")}: {site.Domain}");
        await RestartEngineIfRunningAsync();
    }

    public async Task AddSiteAsync()
    {
        var normalized = SiteManager.Normalize(NewSite);
        if (normalized is null)
        {
            SiteHint = "Некорректный домен. Пример: example.com";
            return;
        }

        _siteManager.Add(normalized);
        NewSite = "";
        SiteHint = "";
        ReloadSites();
        Log.Info($"Добавлен сайт: {normalized}");
        await RestartEngineIfRunningAsync();
    }

    public async Task RemoveSiteAsync(SiteEntry site)
    {
        _siteManager.Remove(site.Domain);
        ReloadSites();
        Log.Info($"Удалён сайт: {site.Domain}");
        await RestartEngineIfRunningAsync();
    }

    private async Task RestartEngineIfRunningAsync()
    {
        if (_runner.State == RunnerState.Running && SelectedStrategy is not null)
        {
            await _runner.StartAsync(SelectedStrategy, AppPaths.EngineDir, AutoRestart);
            RefreshStatus();
        }
    }

    public void SaveSettings() => _settingsStore.Save(_settings);

    public void Shutdown()
    {
        _uptimeTimer.Stop();
        SaveSettings();
        _runner.Dispose();
    }

    private void RefreshStatus()
    {
        switch (_runner.State)
        {
            case RunnerState.Starting:
                StatusText = "Запуск…";
                StatusDetail = SelectedStrategy?.Name ?? "";
                StatusBrush = (Brush)System.Windows.Application.Current.Resources["Amber"];
                break;

            case RunnerState.Running:
                StatusText = "Работает";
                StatusDetail = $"{_runner.CurrentStrategy?.Name ?? "—"} · {FormatUptime(_runner.Uptime)}";
                StatusBrush = (Brush)System.Windows.Application.Current.Resources["Green"];
                break;

            case RunnerState.Error:
                StatusText = "Ошибка";
                StatusDetail = _runner.LastError ?? "winws завершился";
                StatusBrush = (Brush)System.Windows.Application.Current.Resources["Red"];
                break;

            case RunnerState.Stopping:
                StatusText = "Останавливаюсь…";
                StatusDetail = "";
                StatusBrush = (Brush)System.Windows.Application.Current.Resources["Amber"];
                break;

            default:
                StatusText = "Выключено";
                StatusDetail = "Обход DPI не активен";
                StatusBrush = (Brush)System.Windows.Application.Current.Resources["Muted"];
                break;
        }

        IsRunning = _runner.State is RunnerState.Running or RunnerState.Starting;
    }

    private void AppendLog(string line)
    {
        Logs.Add(line);
        while (Logs.Count > 800)
        {
            Logs.RemoveAt(0);
        }
    }

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}"
            : $"{uptime.Minutes:00}:{uptime.Seconds:00}";

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось открыть проводник", ex);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
