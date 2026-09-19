namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Comment styles for comment removal.
/// </summary>
public enum CommentStyle
{
    /// <summary>
    /// INI-style comments (semicolon).
    /// </summary>
    IniStyle,

    /// <summary>
    /// C-style comments (double slash).
    /// </summary>
    CStyle,

    /// <summary>
    /// Script-style comments (hash).
    /// </summary>
    ScriptStyle,
}
