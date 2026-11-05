using System.Diagnostics;
using System.Security.Principal;

namespace PrivateProxy.Core.Services;

public static class Elevation
{
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Starts another copy of this executable with a UAC prompt.</summary>
    public static bool RelaunchElevated(string arguments)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppPaths.BaseDir
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Запуск с правами администратора отменён: {ex.Message}");
            return false;
        }
    }
}
