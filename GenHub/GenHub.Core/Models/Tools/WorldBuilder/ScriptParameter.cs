using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A script action or condition parameter.
/// </summary>
public sealed class ScriptParameter
{
    /// <summary>Gets or sets the parameter type.</summary>
    public WorldBuilderConstants.ScriptParameterType Type { get; set; }

    /// <summary>Gets or sets the integer payload.</summary>
    public int IntValue { get; set; }

    /// <summary>Gets or sets the float payload.</summary>
    public float RealValue { get; set; }

    /// <summary>Gets or sets the string payload.</summary>
    public string StringValue { get; set; } = string.Empty;

    /// <summary>Gets or sets the coordinate payload for coordinate parameters.</summary>
    public (float X, float Y, float Z) CoordValue { get; set; }
}
