namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A single attribute of an interface element, preserving source order.
/// </summary>
/// <param name="Name">The attribute name exactly as declared.</param>
/// <param name="Value">The attribute value.</param>
public sealed record RmlAttribute(string Name, string Value);
