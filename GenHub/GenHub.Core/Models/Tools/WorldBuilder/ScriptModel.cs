using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single script: metadata, OR conditions, and true/false actions.
/// </summary>
public sealed class ScriptModel
{
    /// <summary>Gets or sets the script name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the script comment.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Gets or sets the condition comment.</summary>
    public string ConditionComment { get; set; } = string.Empty;

    /// <summary>Gets or sets the action comment.</summary>
    public string ActionComment { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the script is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the script fires once.</summary>
    public bool IsOneShot { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the script runs on easy difficulty.</summary>
    public bool Easy { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the script runs on normal difficulty.</summary>
    public bool Normal { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the script runs on hard difficulty.</summary>
    public bool Hard { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the script is a subroutine.</summary>
    public bool IsSubroutine { get; set; }

    /// <summary>Gets or sets the evaluation delay in seconds.</summary>
    public int DelaySeconds { get; set; }

    /// <summary>Gets the OR condition branches.</summary>
    public List<ScriptOrBranch> OrConditions { get; } = [];

    /// <summary>Gets the true-branch actions.</summary>
    public List<ScriptActionModel> ActionsTrue { get; } = [];

    /// <summary>Gets the false-branch actions.</summary>
    public List<ScriptActionModel> ActionsFalse { get; } = [];
}
