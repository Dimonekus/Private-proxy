using System.Text;

namespace PrivateProxy.Core;

/// <summary>Minimal thread-safe file + in-memory logger.</summary>
public static class Log
{
    private const int MaxMemoryLines = 500;
    private static readonly object Sync = new();
    private static readonly Queue<string> Memory = new();
    private static readonly List<string> Cache = new();

    public static event Action<string>? LineWritten;

    public static IReadOnlyList<string> Recent
    {
        get
        {
            lock (Sync)
            {
                if (Cache.Count != Memory.Count)
                {
                    Cache.Clear();
                    Cache.AddRange(Memory);
                }

                return Cache.ToArray();
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERR ", message);

    public static void Error(string message, Exception ex) =>
        Write("ERR ", $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";

        lock (Sync)
        {
            Memory.Enqueue(line);
            while (Memory.Count > MaxMemoryLines)
            {
                Memory.Dequeue();
            }

            Cache.Clear();
        }

        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            var file = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging must never take the app down.
        }

        try
        {
            LineWritten?.Invoke(line);
        }
        catch
        {
            // Subscriber failures are not our problem.
        }
    }
}
