using System.Text.Json.Serialization;

namespace PrivateProxy.Core.Models;

/// <summary>
/// A single DPI-bypass configuration. The <see cref="Args"/> values are passed
/// verbatim to winws.exe after placeholder expansion.
/// </summary>
public sealed class Strategy
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "General";
    public string Description { get; init; } = "";

    /// <summary>Marks the strategy the app should use by default.</summary>
    public bool Recommended { get; init; }

    public List<string> Args { get; init; } = [];

    [JsonIgnore]
    public string DisplayCategory => string.IsNullOrWhiteSpace(Category) ? "General" : Category;

    public override string ToString() => Name;
}

public sealed class StrategyCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public List<Strategy> Strategies { get; init; } = [];
}
