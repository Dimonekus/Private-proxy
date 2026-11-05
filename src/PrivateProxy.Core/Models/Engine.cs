namespace PrivateProxy.Core.Models;

/// <summary>Pinned description of the official zapret binaries we ship.</summary>
public sealed class EngineManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Version { get; init; } = "";
    public EngineSource? Source { get; init; }
    public List<EngineFileEntry> Files { get; init; } = [];
}

public sealed class EngineSource
{
    public string Repository { get; init; } = "";
    public string Commit { get; init; } = "";
    public string License { get; init; } = "";
    public string DownloadedFrom { get; init; } = "";
}

public sealed class EngineFileEntry
{
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
}

public sealed class EngineFileStatus
{
    public required string Path { get; init; }
    public bool Exists { get; init; }
    public bool HashMatches { get; init; }
    public long ActualSize { get; init; }
    public string? ActualSha256 { get; init; }
}

public sealed class EngineStatus
{
    public bool Present { get; init; }
    public bool Verified { get; init; }
    public string? Version { get; init; }
    public string? Error { get; init; }
    public List<EngineFileStatus> Files { get; init; } = [];

    public string Summary => Verified
        ? $"движок проверен · {Version}"
        : Present
            ? $"движок найден, но не прошёл проверку: {Error}"
            : $"движок не установлен: {Error}";
}
