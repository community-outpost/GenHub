using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single AND condition with its parameters.
/// </summary>
public sealed class ScriptCondition
{
    /// <summary>Gets or sets the condition type id.</summary>
    public int ConditionType { get; set; }

    /// <summary>Gets or sets the internal name key resolved through the chunk table.</summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>Gets the parameters.</summary>
    public List<ScriptParameter> Parameters { get; } = [];
}
