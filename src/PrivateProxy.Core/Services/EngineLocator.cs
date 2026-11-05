using System.Security.Cryptography;
using System.Text.Json;
using PrivateProxy.Core.Models;

namespace PrivateProxy.Core.Services;

/// <summary>
/// Locates the winws engine and verifies every file against the pinned SHA-256
/// hashes in engine/manifest.json. This is what lets us claim the binaries were
/// not tampered with.
/// </summary>
public sealed class EngineLocator
{
    /// <summary>Files that must exist for the engine to be usable.</summary>
    public static readonly string[] RequiredFiles =
    [
        "winws.exe",
        "WinDivert.dll",
        "WinDivert64.sys",
        "cygwin1.dll"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public EngineStatus Inspect()
    {
        var manifest = LoadManifest(out var manifestError);

        var files = new List<EngineFileStatus>();
        var missing = new List<string>();

        if (manifest is null)
        {
            foreach (var required in RequiredFiles)
            {
                var path = Path.Combine(AppPaths.EngineDir, required);
                var exists = File.Exists(path);
                files.Add(new EngineFileStatus { Path = required, Exists = exists });
                if (!exists)
                {
                    missing.Add(required);
                }
            }

            return new EngineStatus
            {
                Present = missing.Count == 0,
                Verified = false,
                Error = manifestError,
                Files = files
            };
        }

        var allMatched = true;
        foreach (var entry in manifest.Files)
        {
            var path = Path.Combine(AppPaths.EngineDir, entry.Path);
            if (!File.Exists(path))
            {
                files.Add(new EngineFileStatus { Path = entry.Path, Exists = false });
                allMatched = false;
                continue;
            }

            var info = new FileInfo(path);
            var hash = ComputeSha256(path);
            var matches = string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase);

            files.Add(new EngineFileStatus
            {
                Path = entry.Path,
                Exists = true,
                HashMatches = matches,
                ActualSize = info.Length,
                ActualSha256 = hash,
                // size is informational; the hash is authoritative
            });

            if (!matches)
            {
                allMatched = false;
            }
        }

        foreach (var required in RequiredFiles)
        {
            if (!files.Any(f => f.Path.Equals(required, StringComparison.OrdinalIgnoreCase)))
            {
                var path = Path.Combine(AppPaths.EngineDir, required);
                files.Add(new EngineFileStatus { Path = required, Exists = File.Exists(path) });
                if (!File.Exists(path))
                {
                    allMatched = false;
                }
            }
        }

        var present = files.All(f => f.Exists);
        return new EngineStatus
        {
            Present = present,
            Verified = present && allMatched,
            Version = manifest.Version,
            Error = present ? (allMatched ? null : "хэши файлов не совпадают с манифестом") : "не хватает файлов",
            Files = files
        };
    }

    private static EngineManifest? LoadManifest(out string? error)
    {
        error = null;
        if (!File.Exists(AppPaths.EngineManifestPath))
        {
            error = "нет engine/manifest.json";
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<EngineManifest>(
                File.ReadAllText(AppPaths.EngineManifestPath), JsonOptions);

            if (manifest is null || manifest.Files.Count == 0)
            {
                error = "манифест пустой";
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            error = $"манифест повреждён: {ex.Message}";
            return null;
        }
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
