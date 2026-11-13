namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Line ending types for text normalization.
/// </summary>
public enum LineEndingType
{
    /// <summary>
    /// Windows line endings (\r\n).
    /// </summary>
    CRLF,

    /// <summary>
    /// Unix/Linux line endings (\n).
    /// </summary>
    LF,

    /// <summary>
    /// Classic Mac line endings (\r).
    /// </summary>
    CR,
}
