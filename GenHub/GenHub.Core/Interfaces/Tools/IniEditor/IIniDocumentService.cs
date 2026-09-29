using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.IniEditor;

/// <summary>
/// Service for parsing, writing, formatting, and validating Generals and Zero Hour INI documents.
/// </summary>
public interface IIniDocumentService
{
    /// <summary>
    /// Parses document text into an in-memory document, recovering from content
    /// errors (missing or unexpected <c>End</c> markers, fields without keys) so
    /// editors can open and repair real-world files. Recovered errors are recorded
    /// on <see cref="IniDocument.ParseErrors"/> and surfaced by validation.
    /// </summary>
    /// <param name="content">The raw file content.</param>
    /// <param name="sourcePath">The optional source path recorded on the document.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    OperationResult<IniDocument> ParseText(string content, string? sourcePath = null);

    /// <summary>
    /// Parses an INI file into an in-memory document with the same recovery behavior
    /// as <see cref="ParseText"/>. Only I/O failures (missing file, access denied)
    /// produce a failed result.
    /// </summary>
    /// <param name="filePath">Path to the .ini file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    Task<OperationResult<IniDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes a document to canonical file text.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The canonical file text.</returns>
    string WriteDocument(IniDocument document);

    /// <summary>
    /// Rewrites an INI file in canonical form, atomically.
    /// Refuses to overwrite when parsing discarded source lines
    /// (<see cref="IniDocument.HasDiscardedContent"/>); missing <c>End</c>
    /// repairs only add markers and are still formatted.
    /// </summary>
    /// <param name="filePath">Path to the .ini file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an in-memory document for structural problems.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="validatedTargetId">Identifier of the validated target.</param>
    /// <returns>The validation result.</returns>
    ValidationResult ValidateDocument(IniDocument document, string validatedTargetId);

    /// <summary>
    /// Validates an INI file for structural problems.
    /// </summary>
    /// <param name="filePath">Path to the .ini file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default);
}
