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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// Service for parsing, writing, formatting, and validating interface style sheets (.rcss).
/// </summary>
public sealed class RcssDocumentService(ILogger<RcssDocumentService> logger) : IRcssDocumentService
{
    private sealed class ParserState
    {
        private readonly List<int> _lineStarts = [0];

        public ParserState(string content, string sourceName)
        {
            Content = content;
            SourceName = sourceName;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] == '\n')
                {
                    _lineStarts.Add(i + 1);
                }
            }
        }

        public string Content { get; }

        public string SourceName { get; }

        public int Index { get; set; }

        public List<string> Errors { get; } = [];

        public List<string> PendingComments { get; } = [];

        public bool HasMore => Index < Content.Length;

        public int LineNumber => LineOf(Index);

        public int LineOf(int index)
        {
            var line = _lineStarts.BinarySearch(index);
            return line >= 0 ? line + 1 : ~line;
        }

        public char Peek()
        {
            return Content[Index];
        }

        public void SkipTrivia()
        {
            while (HasMore)
            {
                if (char.IsWhiteSpace(Peek()))
                {
                    Index++;
                    continue;
                }

                if (!TryReadComment(out var comment))
                {
                    return;
                }

                PendingComments.Add(comment);
            }
        }

        public bool TryReadComment(out string comment)
        {
            comment = string.Empty;
            if (Index + 1 >= Content.Length || Content[Index] != '/' || Content[Index + 1] != '*')
            {
                return false;
            }

            var end = Content.IndexOf("*/", Index + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                Errors.Add($"Line {LineNumber}: Unclosed comment.");
                Index = Content.Length;
                return true;
            }

            // Edge whitespace is normalized so formatted output stays idempotent.
            comment = Content.Substring(Index + 2, end - Index - 2).Trim();
            Index = end + 2;
            return true;
        }

        public List<string> TakeComments()
        {
            var comments = new List<string>(PendingComments);
            PendingComments.Clear();
            return comments;
        }
    }

    private const int IndentSize = 2;
    private const string ImportantSuffix = "!important";
    private const string ImportantKeyword = "important";

    /// <inheritdoc />
    public OperationResult<RcssDocument> ParseText(string content, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stopwatch = Stopwatch.StartNew();
        var sourceName = string.IsNullOrEmpty(sourcePath) ? "(memory)" : sourcePath;
        var state = new ParserState(content, sourceName);
        var document = new RcssDocument { SourcePath = sourcePath };

        state.SkipTrivia();
        document.LeadingComments.AddRange(state.TakeComments());

        while (state.HasMore)
        {
            state.SkipTrivia();
            if (!state.HasMore)
            {
                break;
            }

            if (state.Peek() == '@')
            {
                ParseAtRule(state, document);
            }
            else
            {
                ParseRule(state, document);
            }

            if (state.Errors.Count > 0)
            {
                break;
            }
        }

        if (state.Errors.Count > 0)
        {
            logger.LogWarning("Failed to parse style sheet {Source}: {Error}", sourceName, state.Errors[0]);
            return OperationResult<RcssDocument>.CreateFailure(state.Errors, stopwatch.Elapsed);
        }

        logger.LogInformation(
            "Parsed style sheet {Source} with {Rules} rules and {AtRules} at-rules",
            sourceName,
            document.Rules.Count,
            document.AtRules.Count);
        return OperationResult<RcssDocument>.CreateSuccess(document, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<RcssDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return OperationResult<RcssDocument>.CreateFailure($"Style sheet file not found: {filePath}");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var content = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            if (content.Contains('�'))
            {
                return OperationResult<RcssDocument>.CreateFailure($"File contains replacement characters: {filePath}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParseText(content, filePath);
        }
        catch (DecoderFallbackException ex)
        {
            logger.LogWarning(ex, "Refusing to parse {Path}: non-UTF-8 or ANSI encoding detected", filePath);
            return OperationResult<RcssDocument>.CreateFailure($"Cannot parse file with non-UTF-8 or unsupported ANSI encoding: {filePath}");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to read style sheet file {Path}", filePath);
            return OperationResult<RcssDocument>.CreateFailure($"Failed to read style sheet file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied reading style sheet file {Path}", filePath);
            return OperationResult<RcssDocument>.CreateFailure($"Access denied reading style sheet file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string WriteDocument(RcssDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        WriteComments(builder, document.LeadingComments);

        var first = true;
        foreach (var rule in document.Rules)
        {
            if (!first)
            {
                builder.AppendLine();
            }

            first = false;
            WriteComments(builder, rule.LeadingComments);
            builder.AppendLine(string.Join(",\n", rule.Selectors) + " {");
            foreach (var declaration in rule.Declarations)
            {
                builder.Append(new string(' ', IndentSize)).Append(declaration.Property).Append(": ").Append(declaration.Value);
                if (declaration.Important)
                {
                    builder.Append(' ').Append(ImportantSuffix);
                }

                builder.AppendLine(";");
            }

            WriteIndentedComments(builder, rule.TrailingComments);
            builder.AppendLine("}");
        }

        foreach (var rule in document.AtRules)
        {
            if (!first)
            {
                builder.AppendLine();
            }

            first = false;
            WriteComments(builder, rule.LeadingComments);
            builder.Append(rule.Name);
            if (rule.Prelude.Length > 0)
            {
                builder.Append(' ').Append(rule.Prelude);
            }

            if (!rule.HasBlock)
            {
                builder.AppendLine(";");
                continue;
            }

            if (rule.RawBody is not null)
            {
                builder.AppendLine(" {");
                builder.AppendLine(rule.RawBody);
                builder.AppendLine("}");
                continue;
            }

            builder.AppendLine(" {");
            foreach (var declaration in rule.Declarations)
            {
                builder.Append(new string(' ', IndentSize)).Append(declaration.Property).Append(": ").Append(declaration.Value);
                if (declaration.Important)
                {
                    builder.Append(' ').Append(ImportantSuffix);
                }

                builder.AppendLine(";");
            }

            WriteIndentedComments(builder, rule.TrailingComments);
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<RcssDeclaration>> ParseInlineStyle(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stopwatch = Stopwatch.StartNew();
        var state = new ParserState(content, "(inline style)");
        var declarations = ParseDeclarationList(state, closedByBrace: false);

        if (state.Errors.Count > 0)
        {
            return OperationResult<IReadOnlyList<RcssDeclaration>>.CreateFailure(state.Errors, stopwatch.Elapsed);
        }

        return OperationResult<IReadOnlyList<RcssDeclaration>>.CreateSuccess(declarations, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public string WriteInlineStyle(IEnumerable<RcssDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var parts = new List<string>();
        foreach (var declaration in declarations)
        {
            var part = $"{declaration.Property}: {declaration.Value}";
            if (declaration.Important)
            {
                part += $" {ImportantSuffix}";
            }

            parts.Add(part);
        }

        return parts.Count == 0 ? string.Empty : string.Join("; ", parts) + ";";
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure($"Style sheet file not found: {filePath}", stopwatch.Elapsed);
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
            logger.LogInformation("Formatted style sheet file {Path}", filePath);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to format style sheet file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Failed to format style sheet file: {ex.Message}", stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied formatting style sheet file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Access denied formatting style sheet file: {ex.Message}", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateDocument(RcssDocument document, string validatedTargetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validatedTargetId);
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<ValidationIssue>();

        if (document.Rules.Count == 0 && document.AtRules.Count == 0)
        {
            issues.Add(new ValidationIssue("Style sheet declares no rules.", ValidationSeverity.Warning, validatedTargetId));
        }

        foreach (var rule in document.Rules)
        {
            var label = rule.Selectors.Count == 0 ? "Style rule" : $"Style rule '{rule.Selectors[0]}'";
            if (rule.Declarations.Count == 0)
            {
                issues.Add(new ValidationIssue($"{label} declares no properties.", ValidationSeverity.Warning, validatedTargetId));
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in rule.Declarations)
            {
                if (string.IsNullOrWhiteSpace(declaration.Value))
                {
                    issues.Add(new ValidationIssue($"{label} leaves '{declaration.Property}' empty.", ValidationSeverity.Warning, validatedTargetId));
                }

                if (!seen.Add(declaration.Property.Trim()))
                {
                    issues.Add(new ValidationIssue($"{label} repeats '{declaration.Property}'.", ValidationSeverity.Warning, validatedTargetId));
                }
            }
        }

        return new ValidationResult(validatedTargetId, issues, stopwatch.Elapsed);
    }

    private static void WriteComments(StringBuilder builder, List<string> comments)
    {
        foreach (var comment in comments)
        {
            builder.Append("/* ").Append(comment).AppendLine(" */");
        }
    }

    private static void WriteIndentedComments(StringBuilder builder, List<string> comments)
    {
        foreach (var comment in comments)
        {
            builder.Append(new string(' ', IndentSize)).Append("/* ").Append(comment).AppendLine(" */");
        }
    }

    private static void ParseRule(ParserState state, RcssDocument document)
    {
        var comments = state.TakeComments();
        var startLine = state.LineNumber;
        var prelude = ReadUntilTopLevel(state, '{', ['}', ';']);
        if (prelude == null)
        {
            state.Errors.Add($"Line {startLine}: Expected '{{' to open a style rule.");
            return;
        }

        var selectors = SplitSelectors(StripComments(prelude));
        if (selectors.Count == 0)
        {
            state.Errors.Add($"Line {startLine}: Style rule declares no selectors.");
            return;
        }

        state.Index++;
        var rule = new RcssRule();
        rule.Selectors.AddRange(selectors);
        rule.LeadingComments.AddRange(comments);
        rule.Declarations.AddRange(ParseDeclarationList(state, closedByBrace: true, rule.TrailingComments));
        document.Rules.Add(rule);
    }

    private static void ParseAtRule(ParserState state, RcssDocument document)
    {
        var comments = state.TakeComments();
        var startLine = state.LineNumber;
        var name = ReadAtName(state);
        state.SkipTrivia();
        if (state.Errors.Count > 0)
        {
            return;
        }

        var preludeStart = state.Index;
        var prelude = ReadUntilTopLevel(state, '{', [';']);
        var rule = new RcssAtRule { Name = name };
        rule.LeadingComments.AddRange(comments);

        if (prelude == null)
        {
            if (state.HasMore && state.Peek() == ';')
            {
                rule.Prelude = StripComments(state.Content.Substring(preludeStart, state.Index - preludeStart)).Trim();
                state.Index++;
                document.AtRules.Add(rule);
                return;
            }

            state.Errors.Add($"Line {startLine}: Expected '{{' or ';' after at-rule '{name}'.");
            return;
        }

        rule.Prelude = StripComments(prelude).Trim();
        rule.HasBlock = true;
        state.Index++;
        if (TryReadNestedBlock(state, out var rawBody))
        {
            rule.RawBody = rawBody;
        }
        else
        {
            rule.Declarations.AddRange(ParseDeclarationList(state, closedByBrace: true, rule.TrailingComments));
        }

        document.AtRules.Add(rule);
    }

    private static List<RcssDeclaration> ParseDeclarationList(ParserState state, bool closedByBrace, List<string>? trailingComments = null)
    {
        var declarations = new List<RcssDeclaration>();
        while (true)
        {
            state.SkipTrivia();
            if (state.Errors.Count > 0)
            {
                return declarations;
            }

            if (!state.HasMore)
            {
                if (closedByBrace)
                {
                    state.Errors.Add($"Line {state.LineNumber}: Unclosed declaration block.");
                }

                return declarations;
            }

            if (closedByBrace && state.Peek() == '}')
            {
                state.Index++;
                if (trailingComments is not null)
                {
                    trailingComments.AddRange(state.TakeComments());
                }

                return declarations;
            }

            var declaration = ParseDeclaration(state, closedByBrace);
            if (declaration == null)
            {
                return declarations;
            }

            declarations.Add(declaration);
        }
    }

    private static RcssDeclaration? ParseDeclaration(ParserState state, bool closedByBrace)
    {
        var startLine = state.LineNumber;
        var property = ReadUntilDelimiterOrNull(state, ':');
        if (property == null)
        {
            state.Errors.Add(closedByBrace
                ? $"Line {startLine}: Expected ':' in a property declaration."
                : $"Line {startLine}: Expected ':' in the inline style.");
            return null;
        }

        property = property.Trim();
        if (property.Length == 0)
        {
            state.Errors.Add($"Line {startLine}: Property name is missing.");
            return null;
        }

        state.Index++;
        var value = ReadDeclarationValue(state, closedByBrace);
        if (value == null)
        {
            state.Errors.Add($"Line {startLine}: Property '{property}' is missing its value.");
            return null;
        }

        value = StripComments(value).Trim();
        var (stripped, important) = SplitImportantSuffix(value);
        value = stripped;

        if (value.Length == 0)
        {
            state.Errors.Add($"Line {startLine}: Property '{property}' is missing its value.");
            return null;
        }

        if (state.HasMore && state.Peek() == ';')
        {
            state.Index++;
        }

        return new RcssDeclaration(property, value, important);
    }

    private static string ReadAtName(ParserState state)
    {
        var start = state.Index;
        state.Index++;
        while (state.HasMore && (char.IsLetterOrDigit(state.Peek()) || state.Peek() == '-' || state.Peek() == '_'))
        {
            state.Index++;
        }

        return state.Content.Substring(start, state.Index - start);
    }

    private static string? ReadUntilTopLevel(ParserState state, char opener, char[] stoppers)
    {
        var start = state.Index;
        var depth = 0;
        while (state.HasMore)
        {
            var c = state.Peek();
            if (c == '"' || c == '\'')
            {
                SkipQuoted(state, c);
                continue;
            }

            if (c == '(' || c == '[')
            {
                depth++;
            }
            else if ((c == ')' || c == ']') && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && c == opener)
            {
                return state.Content.Substring(start, state.Index - start).Trim();
            }
            else if (depth == 0 && Array.IndexOf(stoppers, c) >= 0)
            {
                return null;
            }

            state.Index++;
        }

        return null;
    }

    private static string? ReadUntilDelimiterOrNull(ParserState state, char delimiter)
    {
        var start = state.Index;
        while (state.HasMore)
        {
            var c = state.Peek();
            if (c == '"' || c == '\'')
            {
                SkipQuoted(state, c);
                continue;
            }

            if (c == delimiter)
            {
                return state.Content.Substring(start, state.Index - start);
            }

            if (c == '{' || c == '}' || c == ';')
            {
                return null;
            }

            state.Index++;
        }

        return delimiter == ';' ? state.Content.Substring(start, state.Index - start) : null;
    }

    private static string? ReadDeclarationValue(ParserState state, bool closedByBrace)
    {
        var start = state.Index;
        var depth = 0;
        while (state.HasMore)
        {
            var c = state.Peek();
            if (c == '"' || c == '\'')
            {
                SkipQuoted(state, c);
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && (c == ';' || (closedByBrace && c == '}')))
            {
                return state.Content.Substring(start, state.Index - start);
            }
            else if (depth == 0 && c == '{')
            {
                return null;
            }

            state.Index++;
        }

        return state.Content.Substring(start, state.Index - start);
    }

    private static void SkipQuoted(ParserState state, char quote)
    {
        state.Index++;
        while (state.HasMore)
        {
            var c = state.Peek();
            state.Index++;
            if (c == '\\' && state.HasMore)
            {
                state.Index++;
            }
            else if (c == quote)
            {
                return;
            }
        }
    }

    private static bool TryReadNestedBlock(ParserState state, out string? rawBody)
    {
        rawBody = null;
        var start = state.Index;
        var depth = 0;
        var nested = false;
        while (state.HasMore)
        {
            var c = state.Peek();
            if (c == '"' || c == '\'')
            {
                SkipQuoted(state, c);
                continue;
            }

            if (c == '/' && state.Index + 1 < state.Content.Length && state.Content[state.Index + 1] == '*')
            {
                if (!SkipCommentSpan(state))
                {
                    break;
                }

                continue;
            }

            if (c == '{')
            {
                depth++;
                nested = true;
            }
            else if (c == '}')
            {
                if (depth == 0)
                {
                    if (!nested)
                    {
                        state.Index = start;
                        return false;
                    }

                    rawBody = state.Content.Substring(start, state.Index - start).Trim();
                    state.Index++;
                    return true;
                }

                depth--;
            }

            state.Index++;
        }

        state.Index = start;
        return false;
    }

    private static bool SkipCommentSpan(ParserState state)
    {
        var end = state.Content.IndexOf("*/", state.Index + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        state.Index = end + 2;
        return true;
    }

    private static string StripComments(string value)
    {
        if (value.IndexOf("/*", StringComparison.Ordinal) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                builder.Append(c);
                if (c == '\\' && i + 1 < value.Length)
                {
                    builder.Append(value[i + 1]);
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
                builder.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < value.Length && value[i + 1] == '*')
            {
                var end = value.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                builder.Append(' ');
                i = end + 1;
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static (string Value, bool Important) SplitImportantSuffix(string value)
    {
        var bang = -1;
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < value.Length)
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '!')
            {
                bang = i;
            }
        }

        if (bang < 0 || !string.Equals(value.Substring(bang + 1).Trim(), ImportantKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return (value, false);
        }

        return (value.Substring(0, bang).Trim(), true);
    }

    private static List<string> SplitSelectors(string prelude)
    {
        var selectors = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < prelude.Length; i++)
        {
            var c = prelude[i];
            if (c == '"' || c == '\'')
            {
                current.Append(c);
                i = CopyQuoted(prelude, i, current);
                continue;
            }

            if (c == '(' || c == '[')
            {
                depth++;
            }
            else if ((c == ')' || c == ']') && depth > 0)
            {
                depth--;
            }

            if (c == ',' && depth == 0)
            {
                AddSelector(selectors, current);
                continue;
            }

            current.Append(c);
        }

        AddSelector(selectors, current);
        return selectors;
    }

    private static void AddSelector(List<string> selectors, StringBuilder current)
    {
        var selector = current.ToString().Trim();
        current.Clear();
        if (selector.Length > 0)
        {
            selectors.Add(selector);
        }
    }

    private static int CopyQuoted(string text, int index, StringBuilder current)
    {
        var quote = text[index];
        var i = index + 1;
        while (i < text.Length)
        {
            current.Append(text[i]);
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                current.Append(text[i + 1]);
                i += 2;
                continue;
            }

            i++;
            if (text[i - 1] == quote)
            {
                break;
            }
        }

        return i - 1;
    }
}
