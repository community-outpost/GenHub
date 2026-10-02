using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Tools.RmlEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// Service for parsing, writing, formatting, and validating interface (.rml) documents.
/// </summary>
public sealed class RmlDocumentService(ILogger<RmlDocumentService> logger) : IRmlDocumentService
{
    private const int IndentSize = 2;

    /// <inheritdoc />
    public OperationResult<RmlDocument> ParseText(string content, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stopwatch = Stopwatch.StartNew();
        var sourceName = string.IsNullOrEmpty(sourcePath) ? "(memory)" : sourcePath;

        if (string.IsNullOrWhiteSpace(content))
        {
            return OperationResult<RmlDocument>.CreateFailure("Interface document is empty.", stopwatch.Elapsed);
        }

        XDocument? xml = null;
        try
        {
            xml = XDocument.Parse(content, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            logger.LogWarning("Failed to parse interface document {Source}: {Error}", sourceName, ex.Message);
            return OperationResult<RmlDocument>.CreateFailure($"Line {ex.LineNumber}: {ex.Message}", stopwatch.Elapsed);
        }

        var document = new RmlDocument { SourcePath = sourcePath };
        var root = xml.Root;
        if (root == null || !string.Equals(root.Name.LocalName, RmlConstants.Document.Root, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<RmlDocument>.CreateFailure(
                $"Root element must be <{RmlConstants.Document.Root}>.",
                stopwatch.Elapsed);
        }

        foreach (var node in xml.Nodes().OfType<XComment>())
        {
            document.LeadingComments.Add(new RmlComment { Text = node.Value });
        }

        var head = FindSingleChild(root, RmlConstants.Document.Head);
        var body = FindSingleChild(root, RmlConstants.Document.Body);
        if (head.Element == null || body.Element == null)
        {
            var missing = head.Element == null ? RmlConstants.Document.Head : RmlConstants.Document.Body;
            return OperationResult<RmlDocument>.CreateFailure(
                $"Interface document is missing the <{missing}> element.",
                stopwatch.Elapsed);
        }

        if (head.Duplicate || body.Duplicate)
        {
            var duplicated = head.Duplicate ? RmlConstants.Document.Head : RmlConstants.Document.Body;
            return OperationResult<RmlDocument>.CreateFailure(
                $"Interface document declares <{duplicated}> more than once.",
                stopwatch.Elapsed);
        }

        var unexpected = root.Elements().FirstOrDefault(e => !IsHeadOrBody(e));
        if (unexpected != null)
        {
            var line = ((IXmlLineInfo)unexpected).LineNumber;
            return OperationResult<RmlDocument>.CreateFailure(
                $"Line {line}: Unexpected <{unexpected.Name.LocalName}> inside <{RmlConstants.Document.Root}>.",
                stopwatch.Elapsed);
        }

        document.Head = ConvertElement(head.Element);
        document.Body = ConvertElement(body.Element);

        var count = document.AllElements().Count();
        logger.LogInformation("Parsed interface document {Source} with {Count} elements", sourceName, count);
        return OperationResult<RmlDocument>.CreateSuccess(document, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<RmlDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return OperationResult<RmlDocument>.CreateFailure($"Interface file not found: {filePath}");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var content = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            if (content.Contains('�'))
            {
                return OperationResult<RmlDocument>.CreateFailure($"File contains replacement characters: {filePath}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParseText(content, filePath);
        }
        catch (DecoderFallbackException ex)
        {
            logger.LogWarning(ex, "Refusing to parse {Path}: non-UTF-8 or ANSI encoding detected", filePath);
            return OperationResult<RmlDocument>.CreateFailure($"Cannot parse file with non-UTF-8 or unsupported ANSI encoding: {filePath}");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to read interface file {Path}", filePath);
            return OperationResult<RmlDocument>.CreateFailure($"Failed to read interface file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied reading interface file {Path}", filePath);
            return OperationResult<RmlDocument>.CreateFailure($"Access denied reading interface file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string WriteDocument(RmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        foreach (var comment in document.LeadingComments)
        {
            WriteComment(builder, comment.Text, 0);
        }

        builder.AppendLine($"<{RmlConstants.Document.Root}>");
        WriteElement(builder, document.Head, 1);
        WriteElement(builder, document.Body, 1);
        builder.AppendLine($"</{RmlConstants.Document.Root}>");
        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure($"Interface file not found: {filePath}", stopwatch.Elapsed);
        }

        var parseResult = await ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!parseResult.Success || parseResult.Data == null)
        {
            return OperationResult<bool>.CreateFailure(parseResult.Errors, stopwatch.Elapsed);
        }

        var canonical = WriteDocument(parseResult.Data);
        if (canonical.Contains('�'))
        {
            return OperationResult<bool>.CreateFailure("Cannot format file containing replacement characters.", stopwatch.Elapsed);
        }

        try
        {
            await AtomicFile.WriteAllTextAsync(filePath, canonical, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Formatted interface file {Path}", filePath);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to format interface file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Failed to format interface file: {ex.Message}", stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied formatting interface file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Access denied formatting interface file: {ex.Message}", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateDocument(RmlDocument document, string validatedTargetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validatedTargetId);
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<ValidationIssue>();
        var directory = GetDocumentDirectory(document.SourcePath);

        if (!document.Body.Children.OfType<RmlElement>().Any() && string.IsNullOrWhiteSpace(document.Body.InnerText))
        {
            issues.Add(new ValidationIssue("Interface body contains no elements.", ValidationSeverity.Warning, validatedTargetId));
        }

        foreach (var comment in document.LeadingComments)
        {
            ValidateCommentBody(comment.Text, validatedTargetId, validatedTargetId, issues);
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.AllElements())
        {
            ValidateElement(element, validatedTargetId, directory, seenIds, issues);
        }

        return new ValidationResult(validatedTargetId, issues, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            var missing = new ValidationIssue($"Interface file not found: {filePath}", ValidationSeverity.Critical, filePath)
            {
                IssueType = ValidationIssueType.MissingFile,
            };
            return new ValidationResult(filePath, [missing], stopwatch.Elapsed);
        }

        var parseResult = await ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!parseResult.Success || parseResult.Data == null)
        {
            var issues = new List<ValidationIssue>();
            foreach (var error in parseResult.Errors)
            {
                issues.Add(new ValidationIssue(error, ValidationSeverity.Error, filePath)
                {
                    IssueType = ValidationIssueType.CorruptedFile,
                });
            }

            return new ValidationResult(filePath, issues, stopwatch.Elapsed);
        }

        return ValidateDocument(parseResult.Data, filePath);
    }

    private static string? GetDocumentDirectory(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(sourcePath);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void ValidateElement(
        RmlElement element,
        string validatedTargetId,
        string? directory,
        HashSet<string> seenIds,
        List<ValidationIssue> issues)
    {
        var label = DescribeElement(element);
        ValidateElementId(element, label, validatedTargetId, seenIds, issues);
        ValidateStyleSheetLink(element, label, validatedTargetId, directory, issues);
        ValidateImageSource(element, label, validatedTargetId, directory, issues);
        ValidateDataBinding(element, label, validatedTargetId, issues);

        if (string.Equals(element.Tag, RmlConstants.Elements.Select, StringComparison.OrdinalIgnoreCase)
            && element.Elements.All(e => !string.Equals(e.Tag, RmlConstants.Elements.Option, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new ValidationIssue($"{label} declares no options.", ValidationSeverity.Warning, validatedTargetId));
        }

        ValidateAttributeText(element, label, validatedTargetId, issues);
        foreach (var comment in element.Children.OfType<RmlComment>())
        {
            ValidateCommentBody(comment.Text, label, validatedTargetId, issues);
        }
    }

    private static void ValidateAttributeText(RmlElement element, string label, string validatedTargetId, List<ValidationIssue> issues)
    {
        foreach (var attribute in element.Attributes)
        {
            if (ContainsControlCharacter(attribute.Value))
            {
                issues.Add(new ValidationIssue($"{label} attribute '{attribute.Name}' contains control characters that cannot round-trip.", ValidationSeverity.Warning, validatedTargetId));
            }
        }
    }

    private static void ValidateCommentBody(string text, string label, string validatedTargetId, List<ValidationIssue> issues)
    {
        if (text.Contains("--", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue($"{label} contains a comment with '--', which is rewritten on save.", ValidationSeverity.Warning, validatedTargetId));
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateElementId(
        RmlElement element,
        string label,
        string validatedTargetId,
        HashSet<string> seenIds,
        List<ValidationIssue> issues)
    {
        var id = element.ElementId;
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (!seenIds.Add(id))
        {
            issues.Add(new ValidationIssue($"{label} reuses id '{id}'.", ValidationSeverity.Warning, validatedTargetId));
        }
    }

    private static void ValidateStyleSheetLink(
        RmlElement element,
        string label,
        string validatedTargetId,
        string? directory,
        List<ValidationIssue> issues)
    {
        if (!string.Equals(element.Tag, RmlConstants.Document.Link, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(element.GetAttribute(RmlConstants.Attributes.Type), RmlConstants.File.StyleSheetLinkType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var href = element.GetAttribute(RmlConstants.Attributes.Href);
        if (string.IsNullOrWhiteSpace(href))
        {
            issues.Add(new ValidationIssue($"{label} is missing its style sheet reference.", ValidationSeverity.Warning, validatedTargetId));
            return;
        }

        if (directory != null && !IsExternalReference(href) && !File.Exists(Path.Combine(directory, href)))
        {
            issues.Add(new ValidationIssue($"{label} references missing style sheet '{href}'.", ValidationSeverity.Warning, validatedTargetId)
            {
                IssueType = ValidationIssueType.MissingFile,
            });
        }
    }

    private static void ValidateImageSource(
        RmlElement element,
        string label,
        string validatedTargetId,
        string? directory,
        List<ValidationIssue> issues)
    {
        if (!string.Equals(element.Tag, RmlConstants.Elements.Image, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var src = element.GetAttribute(RmlConstants.Attributes.Src);
        if (string.IsNullOrWhiteSpace(src))
        {
            issues.Add(new ValidationIssue($"{label} is missing its image source.", ValidationSeverity.Warning, validatedTargetId));
            return;
        }

        if (directory != null && !IsExternalReference(src) && !IsSpriteReference(src) && !File.Exists(Path.Combine(directory, src)))
        {
            issues.Add(new ValidationIssue($"{label} references missing image '{src}'.", ValidationSeverity.Warning, validatedTargetId)
            {
                IssueType = ValidationIssueType.MissingFile,
            });
        }
    }

    private static void ValidateDataBinding(RmlElement element, string label, string validatedTargetId, List<ValidationIssue> issues)
    {
        var binding = element.GetAttribute(RmlConstants.DataAttributes.For);
        if (!string.IsNullOrWhiteSpace(binding) && !binding.Contains(" in ", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue($"{label} declares a malformed item binding.", ValidationSeverity.Warning, validatedTargetId));
        }
    }

    private static bool IsExternalReference(string reference)
    {
        return reference.StartsWith(RmlConstants.ExternalSchemes.HttpPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(RmlConstants.ExternalSchemes.HttpsPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(RmlConstants.ExternalSchemes.DataPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpriteReference(string reference)
    {
        return !reference.Contains('/') && !reference.Contains('\\') && Path.GetExtension(reference).Length == 0;
    }

    private static string DescribeElement(RmlElement element)
    {
        var id = element.ElementId;
        return string.IsNullOrEmpty(id) ? $"<{element.Tag}>" : $"<{element.Tag} id='{id}'>";
    }

    private static (XElement? Element, bool Duplicate) FindSingleChild(XElement parent, string name)
    {
        XElement? found = null;
        var count = 0;
        foreach (var child in parent.Elements())
        {
            if (string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                found ??= child;
            }
        }

        return (found, count > 1);
    }

    private static bool IsHeadOrBody(XElement element)
    {
        return string.Equals(element.Name.LocalName, RmlConstants.Document.Head, StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, RmlConstants.Document.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static RmlElement ConvertElement(XElement source, bool preserveWhitespace = false)
    {
        var element = new RmlElement { Tag = source.Name.LocalName };
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            element.Attributes.Add(new RmlAttribute(attribute.Name.LocalName, attribute.Value));
        }

        var verbatim = preserveWhitespace || IsVerbatimElement(element);
        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    element.Children.Add(ConvertElement(child, verbatim));
                    break;
                case XText text when verbatim || !string.IsNullOrWhiteSpace(text.Value):
                    element.Children.Add(new RmlText { Text = text.Value });
                    break;
                case XCData cdata when verbatim || !string.IsNullOrWhiteSpace(cdata.Value):
                    element.Children.Add(new RmlText { Text = cdata.Value });
                    break;
                case XComment comment:
                    element.Children.Add(new RmlComment { Text = comment.Value });
                    break;
                default:
                    // Processing instructions and document type nodes carry no editable content.
                    break;
            }
        }

        return element;
    }

    private static bool IsVerbatimElement(RmlElement element)
    {
        if (string.Equals(element.Tag, RmlConstants.Elements.Preformatted, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(element.GetAttribute(RmlConstants.Whitespace.SpaceAttribute), RmlConstants.Whitespace.Preserve, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteElement(StringBuilder builder, RmlElement element, int depth)
    {
        var indent = new string(' ', depth * IndentSize);
        builder.Append(indent).Append('<').Append(element.Tag);
        foreach (var attribute in element.Attributes)
        {
            builder.Append(' ').Append(attribute.Name).Append("=\"").Append(EscapeAttribute(attribute.Value)).Append('"');
        }

        if (element.Children.Count == 0)
        {
            if (element.IsVoid)
            {
                builder.AppendLine(" />");
            }
            else
            {
                builder.Append('>').Append('<').Append('/').Append(element.Tag).AppendLine(">");
            }

            return;
        }

        var verbatim = IsVerbatimElement(element);
        if (element.Children.Count == 1 && element.Children[0] is RmlText single && (verbatim || !single.Text.Contains('\n')))
        {
            var body = verbatim ? single.Text : single.Text.Trim();
            builder.Append('>').Append(EscapeText(body)).Append('<').Append('/').Append(element.Tag).AppendLine(">");
            return;
        }

        builder.AppendLine(">");
        foreach (var child in element.Children)
        {
            switch (child)
            {
                case RmlElement nested:
                    WriteElement(builder, nested, depth + 1);
                    break;
                case RmlText text:
                    WriteTextNode(builder, text.Text, depth + 1, verbatim);
                    break;
                case RmlComment comment:
                    WriteComment(builder, comment.Text, depth + 1);
                    break;
                default:
                    // Unknown node kinds are skipped so writing never fails on future extensions.
                    break;
            }
        }

        builder.Append(indent).Append('<').Append('/').Append(element.Tag).AppendLine(">");
    }

    private static void WriteTextNode(StringBuilder builder, string text, int depth, bool verbatim = false)
    {
        var lines = text.Split('\n');
        foreach (var raw in lines)
        {
            if (verbatim)
            {
                builder.AppendLine(EscapeText(raw.TrimEnd('\r')));
                continue;
            }

            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            builder.Append(new string(' ', depth * IndentSize)).AppendLine(EscapeText(line));
        }
    }

    private static void WriteComment(StringBuilder builder, string text, int depth)
    {
        builder.Append(new string(' ', depth * IndentSize)).Append("<!--").Append(SanitizeComment(text)).AppendLine("-->");
    }

    private static string SanitizeComment(string text)
    {
        var sanitized = text.Replace("--", "- -", StringComparison.Ordinal);
        return sanitized.EndsWith('-') ? sanitized + " " : sanitized;
    }

    private static string EscapeAttribute(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string EscapeText(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
