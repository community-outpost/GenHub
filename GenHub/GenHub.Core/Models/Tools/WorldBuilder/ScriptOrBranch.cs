using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One OR branch: a list of AND conditions.
/// </summary>
public sealed class ScriptOrBranch
{
    /// <summary>Gets the AND conditions.</summary>
    public List<ScriptCondition> Conditions { get; } = [];
}
