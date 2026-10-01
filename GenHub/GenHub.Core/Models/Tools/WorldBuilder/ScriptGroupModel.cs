using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A named group of scripts.
/// </summary>
public sealed class ScriptGroupModel
{
    /// <summary>Gets or sets the group name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the group is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the group is a subroutine.</summary>
    public bool IsSubroutine { get; set; }

    /// <summary>Gets the scripts in the group.</summary>
    public List<ScriptModel> Scripts { get; } = [];
}
