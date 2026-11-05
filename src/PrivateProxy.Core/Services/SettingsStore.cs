using System.Text.Json;

namespace PrivateProxy.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public Models.AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return new Models.AppSettings();
            }

            return JsonSerializer.Deserialize<Models.AppSettings>(
                       File.ReadAllText(AppPaths.SettingsPath), JsonOptions)
                   ?? new Models.AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn($"Настройки не прочитаны, беру значения по умолчанию: {ex.Message}");
            return new Models.AppSettings();
        }
    }

    public void Save(Models.AppSettings settings)
    {
        try
        {
            AppPaths.EnsureCreated();
            var temp = AppPaths.SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, AppPaths.SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось сохранить настройки", ex);
        }
    }
}
