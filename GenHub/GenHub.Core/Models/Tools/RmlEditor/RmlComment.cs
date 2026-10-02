namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A comment preserved inside an interface document.
/// </summary>
public sealed class RmlComment : RmlNode
{
    /// <summary>
    /// Gets or sets the comment body without the delimiters.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
