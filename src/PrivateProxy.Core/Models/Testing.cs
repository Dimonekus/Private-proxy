namespace PrivateProxy.Core.Models;

public sealed class TargetEndpoint
{
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
}

public sealed class TargetList
{
    public List<TargetEndpoint> Targets { get; init; } = [];
}

public sealed class StrategyTestResult
{
    public required string StrategyId { get; init; }
    public required string StrategyName { get; init; }
    public bool Started { get; init; }
    public int Succeeded { get; init; }
    public int Total { get; init; }
    public double AverageLatencyMs { get; init; }
    public List<string> FailedTargets { get; init; } = [];
    public string? Error { get; init; }

    public bool IsSuccess => Started && Total > 0 && Succeeded == Total;

    public string Summary => Started
        ? $"{Succeeded}/{Total} OK · {AverageLatencyMs:0} ms"
        : $"не запустилась: {Error}";
}
