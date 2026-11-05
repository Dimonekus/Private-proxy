using PrivateProxy.Core.Models;

namespace PrivateProxy.Core.Services;

/// <summary>
/// Manages the domain lists the engine uses to decide which sites to desync.
/// Shipped lists (list-general.txt, list-google.txt, list-exclude.txt) provide
/// the defaults; the user edits list-general-user.txt (add) and
/// list-exclude-user.txt (disable) from the UI.
/// </summary>
public sealed class SiteManager
{
    public IReadOnlyList<SiteEntry> Load()
    {
        var general = ReadDomains(Path.Combine(AppPaths.ListsDir, "list-general.txt"));
        var google = ReadDomains(Path.Combine(AppPaths.ListsDir, "list-google.txt"));
        var custom = ReadDomains(Path.Combine(AppPaths.ListsDir, "list-general-user.txt"));

        var exclude = ReadDomains(Path.Combine(AppPaths.ListsDir, "list-exclude.txt"));
        exclude.UnionWith(ReadDomains(Path.Combine(AppPaths.ListsDir, "list-exclude-user.txt")));

        var bypass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bypass.UnionWith(general);
        bypass.UnionWith(google);
        bypass.UnionWith(custom);

        // Show everything the user can act on: active bypass domains plus the
        // ones they explicitly disabled, so they can be re-enabled.
        var all = new HashSet<string>(bypass, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(ReadDomains(Path.Combine(AppPaths.ListsDir, "list-exclude-user.txt")));

        return all
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => new SiteEntry
            {
                Domain = d,
                Enabled = !exclude.Contains(d),
                IsCustom = custom.Contains(d)
            })
            .ToList();
    }

    public void SetEnabled(string domain, bool enabled)
    {
        if (enabled)
        {
            RemoveLine(Path.Combine(AppPaths.ListsDir, "list-exclude-user.txt"), domain);

            // If the domain is not part of any bypass list, make it bypassable.
            var bypass = ReadDomains(Path.Combine(AppPaths.ListsDir, "list-general.txt"));
            bypass.UnionWith(ReadDomains(Path.Combine(AppPaths.ListsDir, "list-google.txt")));
            var custom = ReadDomains(Path.Combine(AppPaths.ListsDir, "list-general-user.txt"));

            if (!bypass.Contains(domain) && !custom.Contains(domain))
            {
                AddLine(Path.Combine(AppPaths.ListsDir, "list-general-user.txt"), domain);
            }
        }
        else
        {
            AddLine(Path.Combine(AppPaths.ListsDir, "list-exclude-user.txt"), domain);
        }
    }

    public void Add(string domain)
    {
        var normalized = Normalize(domain);
        if (normalized is null)
        {
            return;
        }

        RemoveLine(Path.Combine(AppPaths.ListsDir, "list-exclude-user.txt"), normalized);
        AddLine(Path.Combine(AppPaths.ListsDir, "list-general-user.txt"), normalized);
    }

    public void Remove(string domain)
    {
        RemoveLine(Path.Combine(AppPaths.ListsDir, "list-general-user.txt"), domain);
        RemoveLine(Path.Combine(AppPaths.ListsDir, "list-exclude-user.txt"), domain);
    }

    /// <summary>Turns user input into a bare domain (lowercase, no scheme/path).</summary>
    public static string? Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var value = input.Trim().ToLowerInvariant();
        value = value.Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                     .Replace("https://", "", StringComparison.OrdinalIgnoreCase);

        var slash = value.IndexOfAny(new[] { '/', '?', '#' });
        if (slash >= 0)
        {
            value = value[..slash];
        }

        value = value.Trim().Trim('.', '*').Trim();
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..].Trim().Trim('.', '*').Trim();
        }

        if (value.Length == 0 || !value.Contains('.') || value.Contains(' ') || value.Contains(':'))
        {
            return null;
        }

        return value;
    }

    private static HashSet<string> ReadDomains(string path) =>
        new(File.Exists(path)
            ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            : [], StringComparer.OrdinalIgnoreCase);

    private static void AddLine(string path, string domain)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        if (lines.Any(l => string.Equals(l.Trim(), domain, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        lines.Add(domain);
        File.WriteAllLines(path, lines);
    }

    private static void RemoveLine(string path, string domain)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var lines = File.ReadAllLines(path)
            .Where(l => !string.Equals(l.Trim(), domain, StringComparison.OrdinalIgnoreCase))
            .ToList();
        File.WriteAllLines(path, lines);
    }
}
