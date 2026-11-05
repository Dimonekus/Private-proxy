using System.Diagnostics;
using System.Text;
using PrivateProxy.Core.Models;

namespace PrivateProxy.Core.Services;

public enum RunnerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error
}

public sealed record StartOutcome(bool Ok, string? Error);

/// <summary>
/// Owns the winws.exe process and keeps it alive. Restarts use exponential
/// backoff so a broken strategy does not spin the CPU.
/// </summary>
public sealed class WinwsRunner : IDisposable
{
    private readonly object _sync = new();
    private readonly StringBuilder _tail = new();

    private Process? _process;
    private Strategy? _strategy;
    private bool _autoRestart;
    private bool _intentionalStop = true;
    private bool _disposed;
    private bool _launching;
    private DateTimeOffset? _startedAt;
    private int _restartAttempts;
    private CancellationTokenSource? _restartCts;

    public event EventHandler? StateChanged;

    public RunnerState State { get; private set; } = RunnerState.Stopped;
    public string? LastError { get; private set; }
    public Strategy? CurrentStrategy => _strategy;
    public DateTimeOffset? StartedAt => _startedAt;

    public TimeSpan Uptime => _startedAt is { } started && State == RunnerState.Running
        ? DateTimeOffset.Now - started
        : TimeSpan.Zero;

    public string RecentOutput
    {
        get
        {
            lock (_sync)
            {
                return _tail.ToString();
            }
        }
    }

    public async Task<StartOutcome> StartAsync(Strategy strategy, string? engineDir = null, bool autoRestart = true)
    {
        engineDir ??= AppPaths.EngineDir;

        if (!Elevation.IsAdministrator)
        {
            return new StartOutcome(false, "нужны права администратора");
        }

        var exe = Path.Combine(engineDir, "winws.exe");
        if (!File.Exists(exe))
        {
            return new StartOutcome(false, $"не найден {exe}");
        }

        lock (_sync)
        {
            _autoRestart = autoRestart;
        }

        await StopAsync();

        lock (_sync)
        {
            _strategy = strategy;
            _intentionalStop = false;
            _launching = false;
            _restartAttempts = 0;
            _tail.Clear();
            LastError = null;
            State = RunnerState.Starting;
        }

        RaiseStateChanged();

        var outcome = await LaunchAsync(strategy, engineDir);

        if (!outcome.Ok)
        {
            lock (_sync)
            {
                State = RunnerState.Error;
                LastError = outcome.Error;
            }

            RaiseStateChanged();
        }

        return outcome;
    }

    private async Task<StartOutcome> LaunchAsync(Strategy strategy, string engineDir)
    {
        Process process;
        try
        {
            process = new Process
            {
                StartInfo = BuildStartInfo(strategy, engineDir),
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) => OnOutput(e.Data);
            process.ErrorDataReceived += (_, e) => OnOutput(e.Data);
            process.Exited += (_, _) => OnProcessExited(process);

            if (!process.Start())
            {
                return new StartOutcome(false, "winws.exe не запустился");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось запустить winws", ex);
            return new StartOutcome(false, ex.Message);
        }

        lock (_sync)
        {
            _launching = true;
            _process = process;
        }

        // winws exits almost immediately when the arguments or the driver are bad.
        await Task.Delay(800).ConfigureAwait(false);

        if (process.HasExited)
        {
            var exitCode = SafeExitCode(process);
            var detail = RecentOutput.Trim();
            var message = $"winws завершился с кодом {exitCode}";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += $": {LastLine(detail)}";
            }

            Log.Error(message);

            lock (_sync)
            {
                _launching = false;
                if (ReferenceEquals(process, _process))
                {
                    _process = null;
                }
            }

            SafeDispose(process);
            return new StartOutcome(false, message);
        }

        lock (_sync)
        {
            _launching = false;
            State = RunnerState.Running;
            _startedAt = DateTimeOffset.Now;
        }

        Log.Info($"Запущена стратегия «{strategy.Name}» (pid {SafePid(process)})");
        RaiseStateChanged();
        return new StartOutcome(true, null);
    }

    private static ProcessStartInfo BuildStartInfo(Strategy strategy, string engineDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(engineDir, "winws.exe"),
            WorkingDirectory = engineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in ExpandArguments(strategy))
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    public static IReadOnlyList<string> ExpandArguments(Strategy strategy) =>
        strategy.Args.Select(AppPaths.Expand).ToArray();

    public static string BuildPreview(Strategy strategy) =>
        "winws.exe " + string.Join(' ', ExpandArguments(strategy).Select(Quote));

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    public async Task StopAsync()
    {
        Process? process;
        CancellationTokenSource? cts;

        lock (_sync)
        {
            _intentionalStop = true;
            _launching = false;
            cts = _restartCts;
            _restartCts = null;
            process = _process;
            _process = null;
        }

        cts?.Cancel();
        cts?.Dispose();

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Не удалось остановить winws: {ex.Message}");
            }
            finally
            {
                SafeDispose(process);
            }
        }

        lock (_sync)
        {
            State = RunnerState.Stopped;
            _startedAt = null;
            _strategy = null;
        }

        RaiseStateChanged();
    }

    private void OnProcessExited(Process process)
    {
        int exitCode = SafeExitCode(process);
        var report = true;

        lock (_sync)
        {
            // Ignore exits for a process we already detached (stop or a failed launch).
            if (_disposed || !ReferenceEquals(process, _process))
            {
                SafeDispose(process);
                return;
            }

            _process = null;

            // The initial 800 ms liveness check owns the error reporting for a
            // launch that failed immediately; do not start the watchdog here.
            if (_launching)
            {
                _startedAt = null;
                report = false;
            }
            else
            {
                var ranLongEnough = _startedAt is { } started && DateTimeOffset.Now - started > TimeSpan.FromSeconds(60);
                if (ranLongEnough)
                {
                    _restartAttempts = 0;
                }

                if (_intentionalStop)
                {
                    State = RunnerState.Stopped;
                    _startedAt = null;
                }
                else if (_autoRestart && _strategy is not null)
                {
                    State = RunnerState.Error;
                    LastError = $"winws неожиданно завершился (код {exitCode}), перезапускаю";
                    ScheduleRestart();
                }
                else
                {
                    State = RunnerState.Error;
                    _startedAt = null;
                    LastError = $"winws завершился (код {exitCode})";
                }
            }
        }

        SafeDispose(process);

        if (report)
        {
            RaiseStateChanged();
        }
    }

    private void ScheduleRestart()
    {
        _restartAttempts++;
        var delaySeconds = Math.Min(30, 2 << Math.Min(_restartAttempts - 1, 4));

        var cts = new CancellationTokenSource();
        _restartCts?.Cancel();
        _restartCts = cts;

        var strategy = _strategy;
        if (strategy is null)
        {
            return;
        }

        Log.Warn($"Перезапуск winws через {delaySeconds} с (попытка {_restartAttempts})");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed || _intentionalStop)
                {
                    return;
                }
            }

            var outcome = await LaunchAsync(strategy, AppPaths.EngineDir);
            if (!outcome.Ok)
            {
                lock (_sync)
                {
                    State = RunnerState.Error;
                    LastError = outcome.Error;
                }

                RaiseStateChanged();
                if (_autoRestart)
                {
                    ScheduleRestart();
                }
            }
        }, cts.Token);
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_sync)
        {
            _tail.AppendLine(line);
            if (_tail.Length > 8000)
            {
                _tail.Remove(0, _tail.Length - 6000);
            }
        }

        Log.Info("winws: " + line.Trim());
    }

    private static string LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? text : lines[^1];
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private static int SafePid(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }

    private static void SafeDispose(Process process)
    {
        try
        {
            process.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }
    }
}
