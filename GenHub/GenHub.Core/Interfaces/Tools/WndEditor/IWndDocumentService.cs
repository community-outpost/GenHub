using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.WndEditor;

/// <summary>
/// Service for parsing, writing, formatting, and validating window definition (.wnd) documents.
/// </summary>
public interface IWndDocumentService
{
    /// <summary>
    /// Parses document text into an in-memory document.
    /// </summary>
    /// <param name="content">The raw file content.</param>
    /// <param name="sourcePath">The optional source path recorded on the document.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    OperationResult<WndDocument> ParseText(string content, string? sourcePath = null);

    /// <summary>
    /// Parses a window definition file into an in-memory document.
    /// </summary>
    /// <param name="filePath">Path to the .wnd file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the parsed document.</returns>
    Task<OperationResult<WndDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes a document to canonical file text.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The canonical file text.</returns>
    string WriteDocument(WndDocument document);

    /// <summary>
    /// Parses raw KEY = VALUE; statements into an ordered property list.
    /// </summary>
    /// <param name="content">The raw statement text.</param>
    /// <returns>Operation result containing the parsed properties.</returns>
    OperationResult<IReadOnlyList<WndProperty>> ParseStatements(string content);

    /// <summary>
    /// Rewrites a window definition file in canonical form, atomically.
    /// </summary>
    /// <param name="filePath">Path to the .wnd file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an in-memory document for structural problems.
    /// </summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="validatedTargetId">Identifier of the validated target.</param>
    /// <returns>The validation result.</returns>
    ValidationResult ValidateDocument(WndDocument document, string validatedTargetId);

    /// <summary>
    /// Validates a window definition file for structural problems.
    /// </summary>
    /// <param name="filePath">Path to the .wnd file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default);
}
