using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for processing text files with various transformations.
/// Supports line ending normalization, comment removal, whitespace optimization, and INI file processing.
/// </summary>
public interface ITextProcessingService
{
    /// <summary>
    /// Processes text content with multiple transformations based on options.
    /// </summary>
    /// <param name="content">The text content to process.</param>
    /// <param name="options">Processing options to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processed text content.</returns>
    Task<string> ProcessTextAsync(
        string content,
        TextProcessingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes line endings to a specific format.
    /// </summary>
    /// <param name="content">The text content to normalize.</param>
    /// <param name="type">Target line ending type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text with normalized line endings.</returns>
    Task<string> NormalizeLineEndingsAsync(
        string content,
        LineEndingType type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes comments from text content based on comment style.
    /// </summary>
    /// <param name="content">The text content to process.</param>
    /// <param name="style">Comment style to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text with comments removed.</returns>
    Task<string> RemoveCommentsAsync(
        string content,
        CommentStyle style,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes whitespace from text content based on mode.
    /// </summary>
    /// <param name="content">The text content to process.</param>
    /// <param name="mode">Whitespace removal mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text with whitespace removed.</returns>
    Task<string> RemoveWhitespaceAsync(
        string content,
        WhitespaceMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes sections of text enclosed between delimiter marker pairs.
    /// </summary>
    /// <param name="content">The text content to process.</param>
    /// <param name="markers">List of [startMarker, endMarker] pairs to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text with delimited sections removed.</returns>
    Task<string> RemoveMarkersAsync(
        string content,
        IReadOnlyList<IReadOnlyList<string>> markers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes INI files by removing comments, normalizing line endings, and cleaning whitespace.
    /// </summary>
    /// <param name="content">The INI file content to optimize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Optimized INI file content.</returns>
    Task<string> OptimizeIniFileAsync(
        string content,
        CancellationToken cancellationToken = default);
}

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
