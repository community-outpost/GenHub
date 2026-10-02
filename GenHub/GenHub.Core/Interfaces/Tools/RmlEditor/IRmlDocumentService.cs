using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.RmlEditor;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.RmlEditor;

/// <summary>
/// Service for parsing, writing, formatting, and validating interface (.rml) documents.
/// </summary>
public interface IRmlDocumentService
{
    /// <summary>
    /// Parses document text into an in-memory document. Parsing canonicalizes the
    /// infoset: namespace declarations, processing instructions, the document type,
    /// and the XML declaration are dropped, CDATA sections become plain text, and
    /// formatting whitespace outside preformatted content is normalized away.
    /// </summary>
    /// <param name="content">The raw file content.</param>
    /// <param name="sourcePath">The optional source path recorded on the document.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    OperationResult<RmlDocument> ParseText(string content, string? sourcePath = null);

    /// <summary>
    /// Parses an interface file into an in-memory document.
    /// </summary>
    /// <param name="filePath">Path to the .rml file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    Task<OperationResult<RmlDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes a document to canonical file text.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The canonical file text.</returns>
    string WriteDocument(RmlDocument document);

    /// <summary>
    /// Rewrites an interface file in canonical form, atomically.
    /// </summary>
    /// <param name="filePath">Path to the .rml file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an in-memory document for structural problems.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="validatedTargetId">Identifier of the validated target.</param>
    /// <returns>The validation result.</returns>
    ValidationResult ValidateDocument(RmlDocument document, string validatedTargetId);

    /// <summary>
    /// Validates an interface file for structural problems.
    /// </summary>
    /// <param name="filePath">Path to the .rml file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default);
}
