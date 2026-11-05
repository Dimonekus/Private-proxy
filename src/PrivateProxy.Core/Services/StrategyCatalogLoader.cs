using System.Text.Json;
using PrivateProxy.Core.Models;

namespace PrivateProxy.Core.Services;

/// <summary>Loads and validates strategy catalogues from data/strategies/*.json.</summary>
public sealed class StrategyCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<Strategy> LoadAll(string? directory = null)
    {
        directory ??= AppPaths.StrategiesDir;
        var result = new List<Strategy>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(directory))
        {
            Log.Warn($"Каталог стратегий не найден: {directory}");
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f))
        {
            try
            {
                var catalog = JsonSerializer.Deserialize<StrategyCatalog>(File.ReadAllText(file), JsonOptions);
                if (catalog?.Strategies is null)
                {
                    continue;
                }

                foreach (var strategy in catalog.Strategies)
                {
                    if (!Validate(strategy, out var error))
                    {
                        Log.Warn($"{Path.GetFileName(file)}: пропущена стратегия «{strategy.Id}»: {error}");
                        continue;
                    }

                    if (!ids.Add(strategy.Id))
                    {
                        Log.Warn($"{Path.GetFileName(file)}: дубликат id «{strategy.Id}» пропущен");
                        continue;
                    }

                    result.Add(strategy);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Не удалось прочитать {Path.GetFileName(file)}", ex);
            }
        }

        return result;
    }

    private static bool Validate(Strategy strategy, out string error)
    {
        if (string.IsNullOrWhiteSpace(strategy.Id))
        {
            error = "пустой id";
            return false;
        }

        if (string.IsNullOrWhiteSpace(strategy.Name))
        {
            error = "пустое имя";
            return false;
        }

        if (strategy.Args.Count == 0)
        {
            error = "нет аргументов";
            return false;
        }

        error = "";
        return true;
    }
}
