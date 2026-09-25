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

        public string PeekRaw()
        {
            return _lines[_index];
        }

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
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            var content = utf8Strict.GetString(bytes);
            if (content.Contains('�'))
            {
                return OperationResult<WndDocument>.CreateFailure($"File contains replacement characters: {filePath}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParseText(content, filePath);
        }
        catch (DecoderFallbackException ex)
        {
            logger.LogWarning(ex, "Refusing to parse {Path}: non-UTF-8 or ANSI encoding detected", filePath);
            return OperationResult<WndDocument>.CreateFailure($"Cannot parse file with non-UTF-8 or unsupported ANSI encoding: {filePath}");
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
    public OperationResult<IReadOnlyList<WndProperty>> ParseStatements(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stopwatch = Stopwatch.StartNew();
        var state = new ParserState(SplitLines(content), "(statements)");
        var properties = new List<WndProperty>();
        foreach (var (statement, lineNumber, terminated) in SplitRawStatements(content))
        {
            if (statement.Trim().Length == 0)
            {
                continue;
            }

            if (!terminated)
            {
                state.AddErrorAt(lineNumber, "Unterminated statement, expected ';'.");
                continue;
            }

            var property = SplitStatement(
                string.Concat(statement, WndConstants.Syntax.StatementTerminator),
                state,
                lineNumber);
            if (property != null)
            {
                properties.Add(property);
            }
        }

        if (state.Errors.Count > 0)
        {
            logger.LogWarning("Failed to parse statements: {Error}", state.Errors[0]);
            return OperationResult<IReadOnlyList<WndProperty>>.CreateFailure(state.Errors, stopwatch.Elapsed);
        }

        return OperationResult<IReadOnlyList<WndProperty>>.CreateSuccess(properties, stopwatch.Elapsed);
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

        if (!File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure($"Window definition file not found: {filePath}", stopwatch.Elapsed);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            _ = utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            logger.LogWarning(ex, "Refusing to format {Path}: non-UTF-8 or ANSI encoding detected", filePath);
            return OperationResult<bool>.CreateFailure(
                $"Cannot format file with non-UTF-8 or unsupported ANSI encoding: {filePath}",
                stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to read window definition file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Failed to read window definition file: {ex.Message}", stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied reading window definition file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Access denied reading window definition file: {ex.Message}", stopwatch.Elapsed);
        }

        var parseResult = await ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!parseResult.Success || parseResult.Data == null)
        {
            return OperationResult<bool>.CreateFailure(parseResult.Errors, stopwatch.Elapsed);
        }

        var canonical = WriteDocument(parseResult.Data);
        if (canonical.Contains('\uFFFD'))
        {
            return OperationResult<bool>.CreateFailure("Cannot format file containing replacement characters.", stopwatch.Elapsed);
        }

        var directory = Path.GetDirectoryName(filePath);
        var tempPath = Path.Combine(directory ?? Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempPath, canonical, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
            logger.LogInformation("Formatted window definition file {Path}", filePath);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            DeleteTempFile(tempPath);
            throw;
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

    private static bool IsCommentStart(string content, int index)
    {
        return index + 1 < content.Length && content[index] == '/' && content[index + 1] == '/';
    }

    private static int SkipComment(string content, int index, ref int lineNumber)
    {
        while (index < content.Length && content[index] != '\n')
        {
            index++;
        }

        if (index < content.Length && content[index] == '\n')
        {
            lineNumber++;
            index++;
        }

        return index;
    }

    private static List<(string Statement, int LineNumber, bool Terminated)> SplitRawStatements(string content)
    {
        var statements = new List<(string Statement, int LineNumber, bool Terminated)>();
        var current = new StringBuilder();
        var inQuotes = false;
        var lineNumber = 1;
        var statementLine = 1;
        var hasStatementContent = false;
        var index = 0;

        while (index < content.Length)
        {
            var ch = content[index];
            if (ch == WndConstants.Syntax.Quote)
            {
                inQuotes = !inQuotes;
            }

            if (!inQuotes && IsCommentStart(content, index))
            {
                index = SkipComment(content, index, ref lineNumber);
                continue;
            }

            if (ch == '\n')
            {
                lineNumber++;
            }

            if (ch == WndConstants.Syntax.StatementTerminator && !inQuotes)
            {
                statements.Add((current.ToString(), statementLine, true));
                current.Clear();
                hasStatementContent = false;
                index++;
                continue;
            }

            if (!hasStatementContent && !char.IsWhiteSpace(ch))
            {
                statementLine = lineNumber;
                hasStatementContent = true;
            }

            current.Append(ch);
            index++;
        }

        var remainder = current.ToString();
        if (remainder.Trim().Length > 0)
        {
            statements.Add((remainder, statementLine, false));
        }

        return statements;
    }

    private static void ParseTopLevel(ParserState state, WndDocument document)
    {
        while (state.HasMore && state.Errors.Count == 0)
        {
            if (!ProcessTopLevelLine(state, document))
            {
                return;
            }
        }
    }

    private static bool ProcessTopLevelLine(ParserState state, WndDocument document)
    {
        var line = state.PeekTrimmed();
        if (line.Length == 0)
        {
            state.Advance();
            return true;
        }

        if (string.Equals(line, WndConstants.BlockTags.StartLayoutBlock, StringComparison.Ordinal))
        {
            state.Advance();
            ParseLayoutBlock(state, document);
            return state.Errors.Count == 0;
        }

        if (string.Equals(line, WndConstants.BlockTags.Window, StringComparison.Ordinal))
        {
            var window = ParseWindow(state);
            if (window != null)
            {
                document.Windows.Add(window);
            }

            return state.Errors.Count == 0;
        }

        if (IsBlockTag(line))
        {
            state.AddError($"Unexpected block tag '{line}' outside a window.");
            return false;
        }

        return ProcessTopLevelProperty(state, document);
    }

    private static bool ProcessTopLevelProperty(ParserState state, WndDocument document)
    {
        var property = ReadStatement(state);
        if (property == null)
        {
            return false;
        }

        if (!string.Equals(property.Key, WndConstants.PropertyKeys.FileVersion, StringComparison.Ordinal))
        {
            state.AddError($"Unexpected top-level property '{property.Key}'.");
            return false;
        }

        document.FileVersion = property.Value;
        return true;
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
            if (!ProcessWindowLine(state, window, ref childrenClosed, out var finished))
            {
                return null;
            }

            if (finished)
            {
                ApplyWindowType(window);
                return window;
            }
        }

        if (state.Errors.Count == 0)
        {
            state.AddError("Unterminated window, expected 'END'.");
        }

        return null;
    }

    private static bool ProcessWindowLine(ParserState state, WndWindow window, ref bool childrenClosed, out bool finished)
    {
        finished = false;
        var line = state.PeekTrimmed();
        if (line.Length == 0)
        {
            state.Advance();
            return true;
        }

        if (string.Equals(line, WndConstants.BlockTags.End, StringComparison.Ordinal))
        {
            state.Advance();
            finished = true;
            return true;
        }

        if (TryParseNestedChild(state, window, line, ref childrenClosed))
        {
            return state.Errors.Count == 0;
        }

        if (TryCloseChildren(state, window, line, ref childrenClosed))
        {
            return true;
        }

        return TryParseWindowProperty(state, window, line);
    }

    private static bool TryParseNestedChild(ParserState state, WndWindow window, string line, ref bool childrenClosed)
    {
        if (string.Equals(line, WndConstants.BlockTags.Child, StringComparison.Ordinal))
        {
            if (childrenClosed)
            {
                state.AddError("Unexpected 'CHILD' after 'ENDALLCHILDREN'.");
                return true;
            }

            var child = ParseChild(state);
            if (child != null)
            {
                window.Children.Add(child);
            }

            return true;
        }

        if (string.Equals(line, WndConstants.BlockTags.Window, StringComparison.Ordinal))
        {
            var nested = ParseWindow(state);
            if (nested != null)
            {
                window.Children.Add(nested);
            }

            return true;
        }

        return false;
    }

    private static bool TryCloseChildren(ParserState state, WndWindow window, string line, ref bool childrenClosed)
    {
        if (!string.Equals(line, WndConstants.BlockTags.EndAllChildren, StringComparison.Ordinal))
        {
            return false;
        }

        state.Advance();
        childrenClosed = true;
        window.HasEndAllChildren = true;
        return true;
    }

    private static bool TryParseWindowProperty(ParserState state, WndWindow window, string line)
    {
        if (IsBlockTag(line))
        {
            state.AddError($"Unexpected block tag '{line}' inside a window.");
            return false;
        }

        var property = ReadStatement(state);
        if (property == null)
        {
            return false;
        }

        window.Properties.Add(property);
        return true;
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
        var inQuotes = false;

        while (state.HasMore)
        {
            var line = inQuotes ? state.PeekRaw() : state.PeekTrimmed();
            if (!inQuotes && IsBlockTag(line))
            {
                state.AddErrorAt(startLine, "Unterminated statement, expected ';'.");
                return null;
            }

            state.Advance();

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == WndConstants.Syntax.Quote)
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == WndConstants.Syntax.StatementTerminator && !inQuotes)
                {
                    var remainder = line.Substring(i + 1).Trim();
                    if (remainder.Length > 0 && !remainder.StartsWith("//", StringComparison.Ordinal))
                    {
                        state.AddErrorAt(startLine, $"Unexpected trailing characters after statement terminator: '{remainder}'.");
                        return null;
                    }

                    accumulator.Append(WndConstants.Syntax.StatementTerminator);
                    return SplitStatement(accumulator.ToString(), state, startLine);
                }

                accumulator.Append(ch);
            }

            accumulator.Append('\n');
        }

        state.AddErrorAt(startLine, "Unterminated statement, expected ';'.");
        return null;
    }

    private static WndProperty? SplitStatement(string statement, ParserState state, int startLine)
    {
        var trimmed = statement.Trim();
        var body = trimmed.EndsWith(WndConstants.Syntax.StatementTerminator)
            ? trimmed.Substring(0, trimmed.Length - 1)
            : trimmed;
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

        if (window.Children.Count > 0 || window.HasEndAllChildren)
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
        var inQuotes = false;
        foreach (var ch in value)
        {
            if (ch == WndConstants.Syntax.Quote)
            {
                AppendPendingSpace(builder, ref pendingSpace);
                inQuotes = !inQuotes;
                builder.Append(ch);
                continue;
            }

            if (inQuotes)
            {
                builder.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            AppendPendingSpace(builder, ref pendingSpace);
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static void AppendPendingSpace(StringBuilder builder, ref bool pendingSpace)
    {
        if (pendingSpace)
        {
            builder.Append(' ');
            pendingSpace = false;
        }
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

        ValidateWindowFlags(window, path, targetPath, issues);
        ValidateWindowTypedValues(window, path, targetPath, issues);
        ValidateWindowControlData(window, path, targetPath, issues);

        for (var i = 0; i < window.Children.Count; i++)
        {
            ValidateWindow(window.Children[i], $"{path}/Child[{i}]", targetPath, issues);
        }
    }

    private static void ValidateWindowFlags(WndWindow window, string path, string targetPath, List<ValidationIssue> issues)
    {
        var status = WndStatusValue.ParseStatus(window.GetProperty(WndConstants.PropertyKeys.Status));
        foreach (var token in status.UnknownTokens)
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} has unrecognized STATUS flag '{token}'.",
                ValidationSeverity.Warning,
                targetPath,
                actual: token));
        }

        var style = WndStatusValue.ParseStyle(window.GetProperty(WndConstants.PropertyKeys.Style));
        foreach (var token in style.UnknownTokens)
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} has unrecognized STYLE flag '{token}'.",
                ValidationSeverity.Warning,
                targetPath,
                actual: token));
        }
    }

    private static void ValidateWindowTypedValues(WndWindow window, string path, string targetPath, List<ValidationIssue> issues)
    {
        ValidateOptionalValue(
            window.GetProperty(WndConstants.PropertyKeys.Font),
            value => WndFontValue.TryParse(value, out _),
            WndConstants.PropertyKeys.Font,
            path,
            targetPath,
            issues);
        ValidateOptionalValue(
            window.GetProperty(WndConstants.PropertyKeys.TextColor),
            value => WndTextColorValue.TryParse(value, out _),
            WndConstants.PropertyKeys.TextColor,
            path,
            targetPath,
            issues);
        ValidateOptionalValue(
            window.GetProperty(WndConstants.PropertyKeys.ImageOffset),
            value => WndImageOffset.TryParse(value, out _),
            WndConstants.PropertyKeys.ImageOffset,
            path,
            targetPath,
            issues);

        var tooltipDelay = window.GetProperty(WndConstants.PropertyKeys.TooltipDelay);
        if (tooltipDelay != null && !int.TryParse(tooltipDelay.Trim(), out _))
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} has an invalid TOOLTIPDELAY value.",
                ValidationSeverity.Warning,
                targetPath));
        }

        ValidateDrawDataValues(window, path, targetPath, issues);
    }

    private static void ValidateDrawDataValues(WndWindow window, string path, string targetPath, List<ValidationIssue> issues)
    {
        var drawDataKeys = new List<string>
        {
            WndConstants.PropertyKeys.EnabledDrawData,
            WndConstants.PropertyKeys.DisabledDrawData,
            WndConstants.PropertyKeys.HiliteDrawData,
        };
        drawDataKeys.AddRange(WndConstants.SubDrawDataKeys.All);
        foreach (var key in drawDataKeys)
        {
            var value = window.GetProperty(key);
            if (value != null && !WndDrawDataSet.TryParse(value, out _))
            {
                issues.Add(new ValidationIssue(
                    $"Window at {path} has an invalid {key} value.",
                    ValidationSeverity.Warning,
                    targetPath));
            }
        }
    }

    private static void ValidateOptionalValue(
        string? value,
        Func<string?, bool> tryParse,
        string key,
        string path,
        string targetPath,
        List<ValidationIssue> issues)
    {
        if (value != null && !tryParse(value))
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} has an invalid {key} value.",
                ValidationSeverity.Warning,
                targetPath));
        }
    }

    private static void ValidateWindowControlData(WndWindow window, string path, string targetPath, List<ValidationIssue> issues)
    {
        var controlType = window.ControlType;
        ValidateControlDataValue(window, WndConstants.PropertyKeys.StaticTextData, controlType == WndControlType.StaticText, path, targetPath, issues);
        ValidateControlDataValue(window, WndConstants.PropertyKeys.TextEntryData, controlType == WndControlType.EntryField, path, targetPath, issues);
        ValidateControlDataValue(
            window,
            WndConstants.PropertyKeys.SliderData,
            controlType == WndControlType.HorzSlider || controlType == WndControlType.VertSlider,
            path,
            targetPath,
            issues);
        ValidateControlDataValue(window, WndConstants.PropertyKeys.ListboxData, controlType == WndControlType.ScrollListBox, path, targetPath, issues);
        ValidateControlDataValue(window, WndConstants.PropertyKeys.ComboBoxData, controlType == WndControlType.ComboBox, path, targetPath, issues);
        ValidateControlDataValue(window, WndConstants.PropertyKeys.RadioButtonData, controlType == WndControlType.RadioButton, path, targetPath, issues);
        ValidateControlDataValue(window, WndConstants.PropertyKeys.TabControlData, controlType == WndControlType.TabControl, path, targetPath, issues);
    }

    private static void ValidateControlDataValue(
        WndWindow window,
        string key,
        bool matchesControlType,
        string path,
        string targetPath,
        List<ValidationIssue> issues)
    {
        var value = window.GetProperty(key);
        if (value == null)
        {
            return;
        }

        if (!matchesControlType)
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} declares {key} but is not a matching control type.",
                ValidationSeverity.Warning,
                targetPath));
            return;
        }

        if (!TryParseControlData(key, value))
        {
            issues.Add(new ValidationIssue(
                $"Window at {path} has an invalid {key} value.",
                ValidationSeverity.Warning,
                targetPath));
        }
    }

    private static bool TryParseControlData(string key, string value)
    {
        return key switch
        {
            WndConstants.PropertyKeys.StaticTextData => WndStaticTextData.TryParse(value, out _),
            WndConstants.PropertyKeys.TextEntryData => WndTextEntryData.TryParse(value, out _),
            WndConstants.PropertyKeys.SliderData => WndSliderData.TryParse(value, out _),
            WndConstants.PropertyKeys.ListboxData => WndListboxData.TryParse(value, out _),
            WndConstants.PropertyKeys.ComboBoxData => WndComboBoxData.TryParse(value, out _),
            WndConstants.PropertyKeys.RadioButtonData => WndRadioButtonData.TryParse(value, out _),
            WndConstants.PropertyKeys.TabControlData => WndTabControlData.TryParse(value, out _),
            _ => true,
        };
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
