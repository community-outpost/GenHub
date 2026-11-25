namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Whitespace removal modes.
/// </summary>
public enum WhitespaceMode
{
    /// <summary>
    /// Remove leading whitespace from lines.
    /// </summary>
    Leading,

    /// <summary>
    /// Remove trailing whitespace from lines.
    /// </summary>
    Trailing,

    /// <summary>
    /// Remove empty lines.
    /// </summary>
    EmptyLines,

    /// <summary>
    /// Remove extra whitespace (multiple spaces to single space).
    /// </summary>
    ExtraOnly,

    /// <summary>
    /// Remove all extra whitespace (trim lines).
    /// </summary>
    All,
}
