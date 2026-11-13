using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Options for text processing operations.
/// </summary>
public class TextProcessingOptions
{
    /// <summary>
    /// Gets or sets the line ending type to force. If null, line endings are not modified.
    /// </summary>
    public LineEndingType? ForceEOL { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to delete comments from the text.
    /// </summary>
    public bool DeleteComments { get; set; }

    /// <summary>
    /// Gets or sets the comment style to use when deleting comments.
    /// </summary>
    public CommentStyle CommentStyle { get; set; } = CommentStyle.IniStyle;

    /// <summary>
    /// Gets or sets a value indicating whether to delete whitespace from the text.
    /// </summary>
    public bool DeleteWhitespace { get; set; }

    /// <summary>
    /// Gets or sets the whitespace removal mode to use.
    /// </summary>
    public WhitespaceMode WhitespaceMode { get; set; } = WhitespaceMode.ExtraOnly;

    /// <summary>
    /// Gets or sets the list of delimiter marker pairs to strip out of the text.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>>? ExcludeMarkersList { get; set; }
}
