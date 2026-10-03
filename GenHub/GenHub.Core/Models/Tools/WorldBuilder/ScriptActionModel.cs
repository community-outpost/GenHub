using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A script action with its parameters.
/// </summary>
public sealed class ScriptActionModel
{
    /// <summary>Gets or sets the action type id.</summary>
    public int ActionType { get; set; }

    /// <summary>Gets or sets the internal name key resolved through the chunk table.</summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>Gets the parameters.</summary>
    public List<ScriptParameter> Parameters { get; } = [];
}
