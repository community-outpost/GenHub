namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A single property declaration inside a style sheet rule.
/// </summary>
/// <param name="Property">The property name exactly as declared.</param>
/// <param name="Value">The property value.</param>
/// <param name="Important">Whether the declaration carries the important flag.</param>
public sealed record RcssDeclaration(string Property, string Value, bool Important = false);
