namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A text run inside an interface element.
/// </summary>
public sealed class RmlText : RmlNode
{
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
