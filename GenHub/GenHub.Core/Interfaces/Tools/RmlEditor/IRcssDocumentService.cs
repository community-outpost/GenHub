using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Core.Models.Validation;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.RmlEditor;

/// <summary>
/// Service for parsing, writing, formatting, and validating interface style sheets (.rcss).
/// </summary>
public interface IRcssDocumentService
{
    /// <summary>
    /// Parses style sheet text into an in-memory document.
    /// </summary>
    /// <param name="content">The raw file content.</param>
    /// <param name="sourcePath">The optional source path recorded on the document.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    OperationResult<RcssDocument> ParseText(string content, string? sourcePath = null);

    /// <summary>
    /// Parses a style sheet file into an in-memory document.
    /// </summary>
    /// <param name="filePath">Path to the .rcss file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    Task<OperationResult<RcssDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes a document to canonical file text.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The canonical file text.</returns>
    string WriteDocument(RcssDocument document);

    /// <summary>
    /// Parses an inline style attribute value into property declarations.
    /// </summary>
    /// <param name="content">The raw style attribute value.</param>
    /// <returns>Operation result containing the parsed declarations.</returns>
    OperationResult<IReadOnlyList<RcssDeclaration>> ParseInlineStyle(string content);

    /// <summary>
    /// Serializes declarations to an inline style attribute value.
    /// </summary>
    /// <param name="declarations">The declarations to serialize.</param>
    /// <returns>The inline style attribute value.</returns>
    string WriteInlineStyle(IEnumerable<RcssDeclaration> declarations);

    /// <summary>
    /// Rewrites a style sheet file in canonical form, atomically.
    /// </summary>
    /// <param name="filePath">Path to the .rcss file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an in-memory style sheet for structural problems.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="validatedTargetId">Identifier of the validated target.</param>
    /// <returns>The validation result.</returns>
    ValidationResult ValidateDocument(RcssDocument document, string validatedTargetId);
}
