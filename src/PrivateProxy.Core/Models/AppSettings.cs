namespace PrivateProxy.Core.Models;

public sealed class AppSettings
{
    public string? SelectedStrategyId { get; set; }

    /// <summary>Automatically restart winws if it dies unexpectedly.</summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>Start the selected strategy as soon as the app starts.</summary>
    public bool StartOnLaunch { get; set; }

    /// <summary>
    /// Whether the engine was running when the app last shut down. Used to
    /// restore the previous working mode after a reboot or restart.
    /// </summary>
    public bool EngineWasRunning { get; set; }

    /// <summary>Closing the window hides it to the tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;
}
