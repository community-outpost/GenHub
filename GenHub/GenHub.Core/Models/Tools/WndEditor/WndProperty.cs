namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A single key/value property of a window definition file. Order is significant and preserved.
/// </summary>
/// <param name="Key">The property key.</param>
/// <param name="Value">The property value with the statement terminator removed.</param>
public sealed record WndProperty(string Key, string Value);
