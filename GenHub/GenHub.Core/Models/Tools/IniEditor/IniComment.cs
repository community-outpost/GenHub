namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// A preserved full-line comment or preprocessor directive (for example <c>#include</c>).
/// </summary>
/// <param name="Text">The comment text without the leading semicolon, or the verbatim directive line.</param>
/// <param name="IsDirective">Whether this is a preprocessor directive written back verbatim.</param>
public sealed record IniComment(string Text, bool IsDirective);
