namespace PrivateProxy.Core.Services;

/// <summary>
/// Copies the shipped domain/IP lists into the writable user profile on first
/// run and refreshes them when the bundled version changes (user "*-user.txt"
/// files are preserved). Missing files are always re-created so winws never
/// fails on a dangling hostlist path.
/// </summary>
public static class ListBootstrap
{
    public static void EnsureLists()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ListsDir);

            if (!Directory.Exists(AppPaths.SeedListsDir))
            {
                Log.Warn($"Нет каталога с исходными списками: {AppPaths.SeedListsDir}");
                return;
            }

            var seedVersion = ReadVersion(AppPaths.SeedListsVersionPath);
            var installedVersion = ReadVersion(AppPaths.ListsVersionPath);
            var refreshShipped = !string.IsNullOrEmpty(seedVersion)
                                 && !string.Equals(seedVersion, installedVersion, StringComparison.Ordinal);

            foreach (var source in Directory.EnumerateFiles(AppPaths.SeedListsDir))
            {
                var fileName = Path.GetFileName(source);
                var destination = Path.Combine(AppPaths.ListsDir, fileName);

                // Files ending in "-user" belong to the user and are never
                // overwritten by a shipped-list update.
                if (IsUserList(fileName))
                {
                    if (!File.Exists(destination))
                    {
                        File.Copy(source, destination);
                    }

                    continue;
                }

                if (File.Exists(destination) && !refreshShipped)
                {
                    continue;
                }

                File.Copy(source, destination, overwrite: true);
                Log.Info($"Списки обновлены: {fileName}");
            }

            if (refreshShipped)
            {
                File.WriteAllText(AppPaths.ListsVersionPath, seedVersion);
            }

            // These files are meant to be edited by the user; winws requires them
            // to exist even when they are empty.
            foreach (var fileName in new[]
                     {
                         "list-general-user.txt",
                         "list-exclude-user.txt",
                         "ipset-exclude-user.txt",
                         "ipset-all-user.txt"
                     })
            {
                var path = Path.Combine(AppPaths.ListsDir, fileName);
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось подготовить списки", ex);
        }
    }

    private static bool IsUserList(string fileName) =>
        fileName.EndsWith("-user.txt", StringComparison.OrdinalIgnoreCase);

    private static string ReadVersion(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }
}
