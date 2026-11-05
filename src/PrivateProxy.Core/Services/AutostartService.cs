using System.Diagnostics;

namespace PrivateProxy.Core.Services;

/// <summary>
/// Autostart is implemented as a Task Scheduler job with the highest run level:
/// the user gets no UAC prompt at logon, and winws always has the rights it needs.
/// </summary>
public static class AutostartService
{
    public const string TaskName = "PrivateProxy";

    public static bool IsEnabled()
    {
        var (exitCode, _) = RunSchtasks(["/Query", "/TN", TaskName]);
        return exitCode == 0;
    }

    public static (bool Ok, string Output) Enable(string? exePath = null)
    {
        exePath ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return (false, "не удалось определить путь к приложению");
        }

        var command = $"\"{exePath}\" --autostart";
        var (exitCode, output) = RunSchtasks(
        [
            "/Create",
            "/TN", TaskName,
            "/TR", command,
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/F"
        ]);

        if (exitCode == 0)
        {
            Log.Info("Автозапуск установлен");
        }
        else
        {
            Log.Warn($"Не удалось установить автозапуск: {output}");
        }

        return (exitCode == 0, output);
    }

    public static (bool Ok, string Output) Disable()
    {
        var (exitCode, output) = RunSchtasks(["/Delete", "/TN", TaskName, "/F"]);
        if (exitCode == 0)
        {
            Log.Info("Автозапуск удалён");
        }
        else
        {
            Log.Warn($"Не удалось удалить автозапуск: {output}");
        }

        return (exitCode == 0, output);
    }

    private static (int ExitCode, string Output) RunSchtasks(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "schtasks не запустился");
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            return (process.HasExited ? process.ExitCode : -1, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
