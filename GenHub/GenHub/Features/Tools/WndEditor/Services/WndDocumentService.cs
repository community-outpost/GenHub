using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Service for parsing, writing, formatting, and validating window definition (.wnd) documents.
/// Format behavior is verified against original Generals and Zero Hour game files.
/// </summary>
public sealed class WndDocumentService(ILogger<WndDocumentService> logger) : IWndDocumentService
{
    private sealed class ParserState
    {
        private readonly string[] _lines;
        private int _index;

        public ParserState(string[] lines, string sourceName)
        {
            _lines = lines;
            SourceName = sourceName.Length == 0 ? "(memory)" : sourceName;
            Errors = [];
        }

        public string SourceName { get; }

        public List<string> Errors { get; }

        public bool HasMore => _index < _lines.Length;

        public int LineNumber => _index + 1;

        public string PeekTrimmed()
        {
            return _lines[_index].Trim();
        }

        public void Advance()
        {
            _index++;
        }

        public void SkipBlankLines()
        {
            while (HasMore && PeekTrimmed().Length == 0)
            {
                Advance();
            }
        }

        public void AddError(string detail)
        {
            AddErrorAt(LineNumber, detail);
        }

        public void AddErrorAt(int lineNumber, string detail)
        {
            Errors.Add($"Line {lineNumber}: {detail}");
        }
    }

    /// <inheritdoc />
    public OperationResult<WndDocument> ParseText(string content, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stopwatch = Stopwatch.StartNew();
        var state = new ParserState(SplitLines(content), sourcePath ?? string.Empty);
        var document = new WndDocument { SourcePath = sourcePath };

        ParseTopLevel(state, document);

        if (state.Errors.Count > 0)
        {
            logger.LogWarning("Failed to parse window definition {Source}: {Error}", state.SourceName, state.Errors[0]);
            return OperationResult<WndDocument>.CreateFailure(state.Errors, stopwatch.Elapsed);
        }

        logger.LogInformation("Parsed window definition {Source} with {Count} top-level windows", state.SourceName, document.Windows.Count);
        return OperationResult<WndDocument>.CreateSuccess(document, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<WndDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return OperationResult<WndDocument>.CreateFailure($"Window definition file not found: {filePath}");
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ParseText(content, filePath);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to read window definition file {Path}", filePath);
            return OperationResult<WndDocument>.CreateFailure($"Failed to read window definition file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied reading window definition file {Path}", filePath);
            return OperationResult<WndDocument>.CreateFailure($"Access denied reading window definition file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string WriteDocument(WndDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        AppendStatement(builder, 0, WndConstants.PropertyKeys.FileVersion, document.FileVersion);

        if (document.LayoutBlock.Count > 0)
        {
            AppendLine(builder, 0, WndConstants.BlockTags.StartLayoutBlock);
            foreach (var property in document.LayoutBlock)
            {
                AppendStatement(builder, 1, property.Key, property.Value);
            }

            AppendLine(builder, 0, WndConstants.BlockTags.EndLayoutBlock);
        }

        foreach (var window in document.Windows)
        {
            WriteWindow(builder, window, 0);
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stopwatch = Stopwatch.StartNew();
        var parseResult = await ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!parseResult.Success || parseResult.Data == null)
        {
            return OperationResult<bool>.CreateFailure(parseResult.Errors, stopwatch.Elapsed);
        }

        var canonical = WriteDocument(parseResult.Data);
        var directory = Path.GetDirectoryName(filePath);
        var tempPath = Path.Combine(directory ?? Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempPath, canonical, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
            logger.LogInformation("Formatted window definition file {Path}", filePath);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to format window definition file {Path}", filePath);
            DeleteTempFile(tempPath);
            return OperationResult<bool>.CreateFailure($"Failed to format window definition file: {ex.Message}", stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied formatting window definition file {Path}", filePath);
            DeleteTempFile(tempPath);
            return OperationResult<bool>.CreateFailure($"Access denied formatting window definition file: {ex.Message}", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateDocument(WndDocument document, string validatedTargetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validatedTargetId);
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<ValidationIssue>();

        if (!string.Equals(document.FileVersion, WndConstants.File.KnownVersion, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(
                $"Unexpected file version '{document.FileVersion}'.",
                ValidationSeverity.Warning,
                validatedTargetId,
                WndConstants.File.KnownVersion,
                document.FileVersion));
        }

        if (document.Windows.Count == 0)
        {
            issues.Add(new ValidationIssue("Document contains no windows.", ValidationSeverity.Warning, validatedTargetId));
        }

        for (var i = 0; i < document.Windows.Count; i++)
        {
            ValidateWindow(document.Windows[i], $"Window[{i}]", validatedTargetId, issues);
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
            var missing = new ValidationIssue($"Window definition file not found: {filePath}", ValidationSeverity.Critical, filePath)
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

    private static string[] SplitLines(string content)
    {
        return content.Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    private static void ParseTopLevel(ParserState state, WndDocument document)
    {
        while (state.HasMore && state.Errors.Count == 0)
        {
            var line = state.PeekTrimmed();
            if (line.Length == 0)
            {
                state.Advance();
                continue;
            }

            if (string.Equals(line, WndConstants.BlockTags.StartLayoutBlock, StringComparison.Ordinal))
            {
                state.Advance();
                ParseLayoutBlock(state, document);
                continue;
            }

            if (string.Equals(line, WndConstants.BlockTags.Window, StringComparison.Ordinal))
            {
                var window = ParseWindow(state);
                if (window != null)
                {
                    document.Windows.Add(window);
                }

                continue;
            }

            if (IsBlockTag(line))
            {
                state.AddError($"Unexpected block tag '{line}' outside a window.");
                return;
            }

            var property = ReadStatement(state);
            if (property == null)
            {
                return;
            }

            if (string.Equals(property.Key, WndConstants.PropertyKeys.FileVersion, StringComparison.Ordinal))
            {
                document.FileVersion = property.Value;
            }
            else
            {
                state.AddError($"Unexpected top-level property '{property.Key}'.");
                return;
            }
        }
    }

    private static void ParseLayoutBlock(ParserState state, WndDocument document)
    {
        while (state.HasMore && state.Errors.Count == 0)
        {
            var line = state.PeekTrimmed();
            if (line.Length == 0)
            {
                state.Advance();
                continue;
            }

            if (string.Equals(line, WndConstants.BlockTags.EndLayoutBlock, StringComparison.Ordinal))
            {
                state.Advance();
                return;
            }

            if (IsBlockTag(line))
            {
                state.AddError($"Unexpected block tag '{line}' inside the layout block.");
                return;
            }

            var property = ReadStatement(state);
            if (property == null)
            {
                return;
            }

            document.LayoutBlock.Add(property);
        }

        if (state.Errors.Count == 0)
        {
            state.AddError("Unterminated layout block, expected 'ENDLAYOUTBLOCK'.");
        }
    }

    private static WndWindow? ParseWindow(ParserState state)
    {
        state.Advance();
        var window = new WndWindow { FileName = state.SourceName };
        var childrenClosed = false;

        while (state.HasMore && state.Errors.Count == 0)
        {
            var line = state.PeekTrimmed();
            if (line.Length == 0)
            {
                state.Advance();
                continue;
            }

            if (string.Equals(line, WndConstants.BlockTags.End, StringComparison.Ordinal))
            {
                state.Advance();
                ApplyWindowType(window);
                return window;
            }

            if (string.Equals(line, WndConstants.BlockTags.Child, StringComparison.Ordinal))
            {
                if (childrenClosed)
                {
                    state.AddError("Unexpected 'CHILD' after 'ENDALLCHILDREN'.");
                    return null;
                }

                var child = ParseChild(state);
                if (child == null)
                {
                    return null;
                }

                window.Children.Add(child);
                continue;
            }

            if (string.Equals(line, WndConstants.BlockTags.EndAllChildren, StringComparison.Ordinal))
            {
                state.Advance();
                childrenClosed = true;
                continue;
            }

            if (IsBlockTag(line))
            {
                state.AddError($"Unexpected block tag '{line}' inside a window.");
                return null;
            }

            var property = ReadStatement(state);
            if (property == null)
            {
                return null;
            }

            window.Properties.Add(property);
        }

        if (state.Errors.Count == 0)
        {
            state.AddError("Unterminated window, expected 'END'.");
        }

        return null;
    }

    private static WndWindow? ParseChild(ParserState state)
    {
        state.Advance();
        state.SkipBlankLines();
        if (!state.HasMore)
        {
            state.AddError("Unterminated child, expected 'WINDOW'.");
            return null;
        }

        if (!string.Equals(state.PeekTrimmed(), WndConstants.BlockTags.Window, StringComparison.Ordinal))
        {
            state.AddError($"Unexpected block tag '{state.PeekTrimmed()}' inside a child, expected 'WINDOW'.");
            return null;
        }

        return ParseWindow(state);
    }

    private static void ApplyWindowType(WndWindow window)
    {
        var declared = window.GetProperty(WndConstants.PropertyKeys.WindowType);
        window.ControlTypeName = declared?.Trim() ?? string.Empty;
    }

    private static WndProperty? ReadStatement(ParserState state)
    {
        var startLine = state.LineNumber;
        var accumulator = new StringBuilder();

        while (state.HasMore)
        {
            var line = state.PeekTrimmed();
            if (IsBlockTag(line))
            {
                state.AddErrorAt(startLine, "Unterminated statement, expected ';'.");
                return null;
            }

            if (accumulator.Length > 0)
            {
                accumulator.Append('\n');
            }

            accumulator.Append(line);
            state.Advance();

            if (line.EndsWith(WndConstants.Syntax.StatementTerminator))
            {
                return SplitStatement(accumulator.ToString(), state, startLine);
            }
        }

        state.AddErrorAt(startLine, "Unterminated statement, expected ';'.");
        return null;
    }

    private static WndProperty? SplitStatement(string statement, ParserState state, int startLine)
    {
        var body = statement.Substring(0, statement.Length - 1);
        var separatorIndex = body.IndexOf(WndConstants.Syntax.KeyValueSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            state.AddErrorAt(startLine, $"Invalid statement '{body.Trim()}', expected 'KEY = VALUE;'.");
            return null;
        }

        var key = body.Substring(0, separatorIndex).Trim();
        var value = body.Substring(separatorIndex + 1).Trim();
        if (key.Length == 0)
        {
            state.AddErrorAt(startLine, "Statement has an empty key.");
            return null;
        }

        return new WndProperty(key, value);
    }

    private static bool IsBlockTag(string line)
    {
        return string.Equals(line, WndConstants.BlockTags.StartLayoutBlock, StringComparison.Ordinal)
            || string.Equals(line, WndConstants.BlockTags.EndLayoutBlock, StringComparison.Ordinal)
            || string.Equals(line, WndConstants.BlockTags.Window, StringComparison.Ordinal)
            || string.Equals(line, WndConstants.BlockTags.End, StringComparison.Ordinal)
            || string.Equals(line, WndConstants.BlockTags.Child, StringComparison.Ordinal)
            || string.Equals(line, WndConstants.BlockTags.EndAllChildren, StringComparison.Ordinal);
    }

    private static void WriteWindow(StringBuilder builder, WndWindow window, int indentLevel)
    {
        AppendLine(builder, indentLevel, WndConstants.BlockTags.Window);
        foreach (var property in window.Properties)
        {
            AppendStatement(builder, indentLevel + 1, property.Key, property.Value);
        }

        foreach (var child in window.Children)
        {
            AppendLine(builder, indentLevel + 1, WndConstants.BlockTags.Child);
            WriteWindow(builder, child, indentLevel + 1);
        }

        if (window.Children.Count > 0)
        {
            AppendLine(builder, indentLevel + 1, WndConstants.BlockTags.EndAllChildren);
        }

        AppendLine(builder, indentLevel, WndConstants.BlockTags.End);
    }

    private static void AppendStatement(StringBuilder builder, int indentLevel, string key, string value)
    {
        AppendIndent(builder, indentLevel);
        builder.Append(key);
        builder.Append(' ');
        builder.Append(WndConstants.Syntax.KeyValueSeparator);
        builder.Append(' ');
        builder.Append(NormalizeValue(value));
        builder.Append(WndConstants.Syntax.StatementTerminator);
        builder.Append('\n');
    }

    private static void AppendLine(StringBuilder builder, int indentLevel, string line)
    {
        AppendIndent(builder, indentLevel);
        builder.Append(line);
        builder.Append('\n');
    }

    private static void AppendIndent(StringBuilder builder, int indentLevel)
    {
        for (var i = 0; i < indentLevel; i++)
        {
            builder.Append(WndConstants.Syntax.Indent);
        }
    }

    private static string NormalizeValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static void ValidateWindow(WndWindow window, string path, string targetPath, List<ValidationIssue> issues)
    {
        var declaredType = window.GetProperty(WndConstants.PropertyKeys.WindowType);
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            issues.Add(new ValidationIssue($"Window at {path} has no WINDOWTYPE property.", ValidationSeverity.Error, targetPath));
        }
        else if (WndWindow.ParseControlType(declaredType.Trim()) == WndControlType.Unknown)
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} has unrecognized control type '{declaredType.Trim()}'.",
                ValidationSeverity.Warning,
                targetPath,
                actual: declaredType.Trim()));
        }

        var screenRect = window.GetProperty(WndConstants.PropertyKeys.ScreenRect);
        if (screenRect != null && !WndScreenRect.TryParse(screenRect, out _))
        {
            issues.Add(new ValidationIssue($"Window at {path} has an invalid SCREENRECT value.", ValidationSeverity.Warning, targetPath));
        }

        for (var i = 0; i < window.Children.Count; i++)
        {
            ValidateWindow(window.Children[i], $"{path}/Child[{i}]", targetPath, issues);
        }
    }

    private static void DeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup only.
        }
    }
}
