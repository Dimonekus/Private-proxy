namespace PrivateProxy.Core.Models;

public enum DiagnosticStatus
{
    Ok,
    Warn,
    Fail
}

/// <summary>A single self-contained check in the diagnostics report.</summary>
public sealed class DiagnosticResult
{
    public required string Name { get; init; }
    public DiagnosticStatus Status { get; init; }
    public string Detail { get; init; } = "";
}
