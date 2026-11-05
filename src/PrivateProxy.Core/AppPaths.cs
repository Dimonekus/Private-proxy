namespace PrivateProxy.Core;

/// <summary>
/// Well-known locations used by the application. Shipped read-only data lives
/// next to the executable; everything the user can edit or that must survive an
/// update lives in %LOCALAPPDATA%\PrivateProxy.
/// </summary>
public static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;

    /// <summary>Shipped read-only data: strategies, seed lists, test targets.</summary>
    public static string DataDir { get; } = ResolveDataDir();

    public static string StrategiesDir => Path.Combine(DataDir, "strategies");
    public static string SeedListsDir => Path.Combine(DataDir, "lists");

    /// <summary>Engine root (the folder that contains manifest.json and bin/).</summary>
    public static string EngineRoot { get; } = ResolveEngineRoot();

    public static string EngineDir => Path.Combine(EngineRoot, "bin");
    public static string EngineManifestPath => Path.Combine(EngineRoot, "manifest.json");

    /// <summary>Per-user writable state: settings, logs and editable lists.</summary>
    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrivateProxy");

    public static string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    public static string LogsDir => Path.Combine(ConfigDir, "logs");

    /// <summary>Writable copy of the domain/IP lists that winws actually reads.</summary>
    public static string ListsDir => Path.Combine(ConfigDir, "lists");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>Expands the placeholders that are allowed in strategy arguments.</summary>
    public static string Expand(string value) => value
        .Replace("{ENGINE}", EngineDir, StringComparison.OrdinalIgnoreCase)
        .Replace("{DATA}", DataDir, StringComparison.OrdinalIgnoreCase)
        .Replace("{LISTS}", ListsDir, StringComparison.OrdinalIgnoreCase)
        .Replace("{APP}", BaseDir, StringComparison.OrdinalIgnoreCase);

    private static string ResolveDataDir()
    {
        var local = Path.Combine(BaseDir, "data");
        if (LooksLikeDataDir(local))
        {
            return local;
        }

        // Running from src/PrivateProxy.App/bin/Debug/... during development.
        var dir = new DirectoryInfo(BaseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (LooksLikeDataDir(candidate))
            {
                return candidate;
            }
        }

        return local;
    }

    private static bool LooksLikeDataDir(string path) =>
        Directory.Exists(Path.Combine(path, "strategies"));

    private static string ResolveEngineRoot()
    {
        var local = Path.Combine(BaseDir, "engine");
        if (File.Exists(Path.Combine(local, "manifest.json")))
        {
            return local;
        }

        var dir = new DirectoryInfo(BaseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "engine");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
        }

        return local;
    }
}
