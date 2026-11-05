namespace PrivateProxy.Core.Services;

/// <summary>
/// Copies the shipped domain/IP lists into the writable user profile on first
/// run. Missing files are always re-created so winws never fails on a dangling
/// hostlist path.
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

            foreach (var source in Directory.EnumerateFiles(AppPaths.SeedListsDir))
            {
                var fileName = Path.GetFileName(source);
                var destination = Path.Combine(AppPaths.ListsDir, fileName);
                if (!File.Exists(destination))
                {
                    File.Copy(source, destination);
                    Log.Info($"Создан список {fileName}");
                }
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
}
