namespace PrivateProxy.Core.Models;

/// <summary>
/// A domain the user can route through (or exclude from) the DPI-bypass engine.
/// </summary>
public sealed class SiteEntry
{
    public required string Domain { get; init; }

    /// <summary>True when the domain is currently bypassed (in the desync lists and not excluded).</summary>
    public bool Enabled { get; set; }

    /// <summary>True when the user added this domain themselves (editable/removable).</summary>
    public bool IsCustom { get; init; }
}
