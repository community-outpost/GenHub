using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
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

namespace GenHub.Features.Tools.IniEditor.Services;

/// <summary>
/// Service for parsing, writing, formatting, and validating Generals and Zero Hour INI documents.
/// </summary>
public sealed class IniDocumentService(ILogger<IniDocumentService> logger, ILocalizationService localizationService) : IIniDocumentService
{
    /// <summary>
    /// An open block and the indentation of its opening line.
    /// </summary>
    /// <param name="Block">The open block.</param>
    /// <param name="Indent">The indentation of the opening line.</param>
    private sealed record BlockFrame(IniBlock Block, int Indent);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <inheritdoc />
    public OperationResult<IniDocument> ParseText(string content, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var document = new IniDocument { SourcePath = sourcePath };
        var stack = new Stack<BlockFrame>();
        var pendingComments = new List<IniComment>();
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var context = new IniParseContext(lines, document, stack, pendingComments, errors);

        for (var i = 0; i < lines.Length; i++)
        {
            ParseLine(context, i);
        }

        FlushTrailingComments(document, stack, pendingComments);
        CloseUnclosedBlocks(document, stack, errors);

        if (errors.Count > 0)
        {
            document.ParseErrors.AddRange(errors);
            logger.LogWarning(
                "Parsed INI {Source} with {Count} recovered error(s): {Error}",
                sourcePath ?? "(memory)",
                errors.Count,
                errors[0]);
        }
        else
        {
            logger.LogInformation("Parsed INI {Source} with {Count} blocks", sourcePath ?? "(memory)", document.Blocks.Count);
        }

        return OperationResult<IniDocument>.CreateSuccess(document, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IniDocument>> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return OperationResult<IniDocument>.CreateFailure($"INI file not found: {filePath}");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var (content, encoding) = DecodeContent(bytes, filePath);
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = ParseText(content, filePath);
            if (parsed.Success && parsed.Data != null)
            {
                parsed.Data.SourceEncoding = encoding;
            }

            return parsed;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to read INI file {Path}", filePath);
            return OperationResult<IniDocument>.CreateFailure($"Failed to read INI file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied reading INI file {Path}", filePath);
            return OperationResult<IniDocument>.CreateFailure($"Access denied reading INI file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string WriteDocument(IniDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        WriteComments(builder, document.HeaderComments, 0);
        foreach (var field in document.GlobalFields)
        {
            WriteComments(builder, field.LeadingComments, 0);
            AppendLine(builder, 0, AppendTrailingComment($"{field.Key} = {field.Value}", field.TrailingComment));
        }

        if (document.GlobalFields.Count > 0 && document.Blocks.Count > 0)
        {
            builder.Append(IniConstants.Syntax.NewLine);
        }

        foreach (var block in document.Blocks)
        {
            WriteBlock(builder, block, 0);
            builder.Append(IniConstants.Syntax.NewLine);
        }

        WriteComments(builder, document.TrailingComments, 0);
        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> FormatFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure($"INI file not found: {filePath}", stopwatch.Elapsed);
        }

        var parseResult = await ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!parseResult.Success || parseResult.Data == null)
        {
            return OperationResult<bool>.CreateFailure(parseResult.Errors, stopwatch.Elapsed);
        }

        if (parseResult.Data.HasDiscardedContent)
        {
            logger.LogWarning("Refusing to format INI file {Path}: recovery discarded source lines", filePath);
            return OperationResult<bool>.CreateFailure(
                parseResult.Data.ParseErrors.Prepend(
                    localizationService.GetString("Tools.IniEditor.Format.RefusedDiscardedContent")),
                stopwatch.Elapsed);
        }

        var canonical = WriteDocument(parseResult.Data);
        try
        {
            await AtomicFile.WriteAllTextAsync(filePath, canonical, parseResult.Data.SourceEncoding, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Formatted INI file {Path}", filePath);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to format INI file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Failed to format INI file: {ex.Message}", stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied formatting INI file {Path}", filePath);
            return OperationResult<bool>.CreateFailure($"Access denied formatting INI file: {ex.Message}", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateDocument(IniDocument document, string validatedTargetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validatedTargetId);
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<ValidationIssue>();

        foreach (var parseError in document.ParseErrors)
        {
            issues.Add(new ValidationIssue(parseError, ValidationSeverity.Error, validatedTargetId)
            {
                IssueType = ValidationIssueType.CorruptedFile,
            });
        }

        if (document.Blocks.Count == 0 && document.GlobalFields.Count == 0)
        {
            issues.Add(new ValidationIssue("Document contains no blocks.", ValidationSeverity.Warning, validatedTargetId));
        }

        ValidateSiblingBlocks(document.Blocks, validatedTargetId, issues);

        return new ValidationResult(validatedTargetId, issues, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            var missing = new ValidationIssue($"INI file not found: {filePath}", ValidationSeverity.Critical, filePath)
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

    /// <summary>
    /// Deletes a temp file on a best effort basis.
    /// </summary>
    /// <param name="tempPath">The temp file path.</param>
    internal static void DeleteTempFile(string tempPath)
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
            // Best effort cleanup of the temp file.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp file.
        }
    }

    /// <summary>
    /// Checks if a key represents a valueless bare keyword.
    /// </summary>
    /// <param name="line">The line text to check.</param>
    /// <returns>True if the line matches a valueless keyword; otherwise, false.</returns>
    internal static bool IsValuelessKey(string line) =>
        IniConstants.ValuelessKeys.All.Any(valuelessKey => string.Equals(valuelessKey, line, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Checks if a key is a recognized module keyword.
    /// </summary>
    /// <param name="key">The key name to check.</param>
    /// <returns>True if the key represents a module block; otherwise, false.</returns>
    internal static bool IsModuleKey(string key) =>
        IniConstants.ModuleKeys.All.Any(moduleKey => string.Equals(moduleKey, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Checks if a token represents a recognized block type, module key, or
    /// parameterized engine sub-block.
    /// </summary>
    /// <param name="token">The token to check.</param>
    /// <returns>True if the token is a recognized block or module keyword; otherwise, false.</returns>
    internal static bool IsBlockType(string token) =>
        IniConstants.BlockTypes.All.Any(blockType => string.Equals(blockType, token, StringComparison.OrdinalIgnoreCase)) ||
        IniConstants.SubBlockTypes.All.Any(subType => string.Equals(subType, token, StringComparison.OrdinalIgnoreCase)) ||
        IsModuleKey(token) ||
        IsChallengePersonaBlock(token) ||
        IsRadiusCursorBlock(token);

    /// <summary>
    /// Checks if a token is a numbered challenge mode general persona block.
    /// </summary>
    /// <param name="token">The token to check.</param>
    /// <returns>True for GeneralPersona followed by digits; otherwise, false.</returns>
    internal static bool IsChallengePersonaBlock(string token) =>
        token.StartsWith(IniConstants.SubBlockTypePatterns.GeneralPersonaPrefix, StringComparison.OrdinalIgnoreCase) &&
        token.Length > IniConstants.SubBlockTypePatterns.GeneralPersonaPrefix.Length &&
        token[IniConstants.SubBlockTypePatterns.GeneralPersonaPrefix.Length..].All(char.IsAsciiDigit);

    /// <summary>
    /// Checks if a token is an interface radius cursor block.
    /// </summary>
    /// <param name="token">The token to check.</param>
    /// <returns>True for tokens ending in RadiusCursor with a non-empty stem; otherwise, false.</returns>
    internal static bool IsRadiusCursorBlock(string token) =>
        token.EndsWith(IniConstants.SubBlockTypePatterns.RadiusCursorSuffix, StringComparison.OrdinalIgnoreCase) &&
        token.Length > IniConstants.SubBlockTypePatterns.RadiusCursorSuffix.Length;

    /// <summary>
    /// Calculates the number of leading spaces in a line.
    /// </summary>
    /// <param name="raw">The raw line text.</param>
    /// <returns>The indentation depth in spaces.</returns>
    internal static int GetIndent(string raw)
    {
        var indent = 0;
        foreach (var c in raw)
        {
            if (c == ' ')
            {
                indent++;
            }
            else if (c == '\t')
            {
                indent += 4 - (indent % 4);
            }
            else
            {
                break;
            }
        }

        return indent;
    }

    /// <summary>
    /// Determines whether a <c>Key = Value</c> line opens a nested module block.
    /// Only engine module slot keys open modules. Indentation alone never opens a
    /// module: real-world map overrides use ragged alignment inside flat sections,
    /// and treating an indent increase as nesting swallows the section <c>End</c>.
    /// </summary>
    /// <param name="key">The key of the current line.</param>
    /// <returns>True if the line opens a module block; otherwise, false.</returns>
    internal static bool OpensModuleBlock(string key) => IsModuleKey(key);

    private sealed record IniParseContext(
        string[] Lines,
        IniDocument Document,
        Stack<BlockFrame> Stack,
        List<IniComment> PendingComments,
        List<string> Errors);

    private static void ParseLine(IniParseContext context, int index)
    {
        var raw = context.Lines[index];
        var lineNumber = index + 1;
        var (code, comment) = SplitComment(raw);

        var line = code.Trim();
        if (line.Length == 0)
        {
            if (comment != null)
            {
                context.PendingComments.Add(new IniComment(comment, false));
            }

            return;
        }

        if (line.StartsWith('#'))
        {
            context.PendingComments.Add(new IniComment(line, true));
            return;
        }

        if (string.Equals(line, IniConstants.BlockTags.End, StringComparison.OrdinalIgnoreCase))
        {
            if (comment != null)
            {
                context.PendingComments.Add(new IniComment(comment, false));
            }

            CloseBlock(lineNumber, context.Document, context.Stack, context.PendingComments, context.Errors);
            return;
        }

        var separatorIndex = line.IndexOf(IniConstants.Syntax.KeyValueSeparator);
        if (separatorIndex >= 0 && context.Stack.Count > 0)
        {
            AddFieldOrModule(context, index, line, separatorIndex, lineNumber, comment);
            return;
        }

        if (separatorIndex >= 0)
        {
            AddGlobalField(context, line, separatorIndex, lineNumber, comment);
            return;
        }

        if (context.Stack.Count > 0)
        {
            ParseBlockContentLine(context, line, raw, lineNumber, comment);
            return;
        }

        OpenBlock(context, line, GetIndent(raw), lineNumber, comment);
    }

    private static void ParseBlockContentLine(
        IniParseContext context,
        string line,
        string raw,
        int lineNumber,
        string? comment)
    {
        if (IsValuelessKey(line))
        {
            AddBareField(line, comment, context.Stack, context.PendingComments);
            return;
        }

        if (line.StartsWith(IniConstants.MapDirectives.RemoveModule, StringComparison.OrdinalIgnoreCase))
        {
            var parts = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            var key = parts[0];
            var value = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var field = new IniField(key, value, comment) { IsBare = true };
            field.LeadingComments.AddRange(context.PendingComments);
            context.PendingComments.Clear();
            context.Stack.Peek().Block.Fields.Add(field);
            return;
        }

        var firstToken = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (IsParticleSystemReference(firstToken, context, lineNumber - 1, GetIndent(raw))
            && TryAddWhitespaceField(line, comment, context.Stack, context.PendingComments))
        {
            return;
        }

        if (IsBlockType(firstToken))
        {
            OpenBlock(context, line, GetIndent(raw), lineNumber, comment);
            return;
        }

        if (TryAddWhitespaceField(line, comment, context.Stack, context.PendingComments))
        {
            return;
        }

        AddBareField(line, comment, context.Stack, context.PendingComments);
    }

    private static bool TryAddWhitespaceField(
        string line,
        string? comment,
        Stack<BlockFrame> stack,
        List<IniComment> pendingComments)
    {
        var spaceIndex = line.IndexOfAny([' ', '\t']);
        if (spaceIndex <= 0)
        {
            return false;
        }

        var key = line[..spaceIndex].Trim();
        var value = line[(spaceIndex + 1)..].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        var field = new IniField(key, value, comment);
        field.LeadingComments.AddRange(pendingComments);
        pendingComments.Clear();
        stack.Peek().Block.Fields.Add(field);
        return true;
    }

    private static void AddGlobalField(
        IniParseContext context,
        string line,
        int separatorIndex,
        int lineNumber,
        string? comment)
    {
        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        if (key.Length == 0)
        {
            context.Errors.Add($"Line {lineNumber}: Field is missing a key.");
            context.Document.HasDiscardedContent = true;
            return;
        }

        var field = new IniField(key, value, comment);
        field.LeadingComments.AddRange(context.PendingComments);
        context.PendingComments.Clear();
        if (context.Document.GlobalFields.Count == 0 && context.Document.Blocks.Count == 0)
        {
            context.Document.HeaderComments.AddRange(field.LeadingComments);
            field.LeadingComments.Clear();
        }

        context.Document.GlobalFields.Add(field);
    }

    private static void AddBareField(
        string line,
        string? comment,
        Stack<BlockFrame> stack,
        List<IniComment> pendingComments)
    {
        var field = new IniField(line, string.Empty, comment) { IsBare = true };
        field.LeadingComments.AddRange(pendingComments);
        pendingComments.Clear();
        stack.Peek().Block.Fields.Add(field);
    }

    private static void AddFieldOrModule(
        IniParseContext context,
        int index,
        string line,
        int separatorIndex,
        int lineNumber,
        string? comment)
    {
        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        if (key.Length == 0)
        {
            context.Errors.Add($"Line {lineNumber}: Field is missing a key.");
            context.Document.HasDiscardedContent = true;
            return;
        }

        var indent = GetIndent(context.Lines[index]);
        if (OpensModuleBlock(key) || OpensAnimationBlock(key, context, index, indent))
        {
            OpenModuleBlock(key, value, indent, lineNumber, comment, context.Stack, context.PendingComments);
            return;
        }

        var field = new IniField(key, value, comment);
        field.LeadingComments.AddRange(context.PendingComments);
        context.PendingComments.Clear();
        context.Stack.Peek().Block.Fields.Add(field);
    }

    /// <summary>
    /// Determines whether an <c>Animation = Name</c> line opens a nested animation
    /// sub-block. Animation lines are only structural inside AnimationState and
    /// TransitionState parents that continue with animation block fields; everywhere
    /// else (notably ConditionState modules) they stay plain fields.
    /// </summary>
    /// <param name="key">The key of the current line.</param>
    /// <param name="context">The parsing context.</param>
    /// <param name="index">The zero-based index of the current line.</param>
    /// <param name="indent">The indentation of the current line.</param>
    /// <returns>True if the line opens an animation sub-block; otherwise, false.</returns>
    private static bool OpensAnimationBlock(string key, IniParseContext context, int index, int indent)
    {
        if (!string.Equals(key, IniConstants.ModuleKeys.Animation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (context.Stack.Count == 0)
        {
            return false;
        }

        var parentType = context.Stack.Peek().Block.BlockType;
        var isAnimationParent =
            string.Equals(parentType, IniConstants.ModuleKeys.AnimationState, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentType, IniConstants.ModuleKeys.TransitionState, StringComparison.OrdinalIgnoreCase);
        if (!isAnimationParent)
        {
            return false;
        }

        return HasAnimationBlockFields(context.Lines, index, indent);
    }

    /// <summary>
    /// Determines whether a bare <c>ParticleSystem Name</c> line references an
    /// emitter instead of opening a nested definition. Effect lists carry their
    /// emitters as bare references with no closing <c>End</c>; only a line that
    /// continues with deeper-indented <c>Key = Value</c> content opens a block.
    /// </summary>
    /// <param name="firstToken">The first token of the current line.</param>
    /// <param name="context">The parsing context.</param>
    /// <param name="index">The zero-based index of the current line.</param>
    /// <param name="indent">The indentation of the current line.</param>
    /// <returns>True when the line is an emitter reference; otherwise, false.</returns>
    private static bool IsParticleSystemReference(string firstToken, IniParseContext context, int index, int indent)
    {
        if (!string.Equals(firstToken, IniConstants.SubBlockTypes.ParticleSystem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FindNestedContentKey(context.Lines, index, indent) == null;
    }

    /// <summary>
    /// Looks ahead past blank lines and comments to decide whether an Animation
    /// line heads a nested sub-block: the next significant line must be deeper
    /// indented and carry an animation block field key.
    /// </summary>
    /// <param name="lines">All document lines.</param>
    /// <param name="index">The zero-based index of the Animation line.</param>
    /// <param name="indent">The indentation of the Animation line.</param>
    /// <returns>True when a nested animation sub-block follows; otherwise, false.</returns>
    private static bool HasAnimationBlockFields(string[] lines, int index, int indent)
    {
        var nextKey = FindNestedContentKey(lines, index, indent);
        return nextKey != null && IniConstants.AnimationBlockFields.All.Any(field =>
            string.Equals(field, nextKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the key of the first deeper-indented <c>Key = Value</c> line after
    /// the given index, stopping at <c>End</c>, dedent, or content without a
    /// separator.
    /// </summary>
    /// <param name="lines">All document lines.</param>
    /// <param name="index">The zero-based index of the anchor line.</param>
    /// <param name="indent">The indentation of the anchor line.</param>
    /// <returns>The nested content key, or null when no nested fields follow.</returns>
    private static string? FindNestedContentKey(string[] lines, int index, int indent)
    {
        for (var j = index + 1; j < lines.Length; j++)
        {
            var (code, _) = SplitComment(lines[j]);
            var candidate = code.Trim();
            if (candidate.Length == 0 || candidate.StartsWith('#'))
            {
                continue;
            }

            if (string.Equals(candidate, IniConstants.BlockTags.End, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (GetIndent(lines[j]) <= indent)
            {
                return null;
            }

            var separatorIndex = candidate.IndexOf(IniConstants.Syntax.KeyValueSeparator);
            if (separatorIndex < 0)
            {
                return null;
            }

            return candidate[..separatorIndex].Trim();
        }

        return null;
    }

    private static void OpenModuleBlock(
        string key,
        string value,
        int indent,
        int lineNumber,
        string? comment,
        Stack<BlockFrame> stack,
        List<IniComment> pendingComments)
    {
        var block = new IniBlock
        {
            BlockType = key,
            AssignmentValue = value,
            TrailingComment = comment,
            LineNumber = lineNumber,
        };
        block.LeadingComments.AddRange(pendingComments);
        pendingComments.Clear();
        stack.Push(new BlockFrame(block, indent));
    }

    private static void CloseBlock(
        int lineNumber,
        IniDocument document,
        Stack<BlockFrame> stack,
        List<IniComment> pendingComments,
        List<string> errors)
    {
        if (stack.Count == 0)
        {
            errors.Add($"Line {lineNumber}: Unexpected 'End' without an open block.");
            document.HasDiscardedContent = true;
            return;
        }

        var closed = stack.Pop().Block;
        closed.TrailingComments.AddRange(pendingComments);
        pendingComments.Clear();
        if (stack.Count == 0)
        {
            document.Blocks.Add(closed);
        }
        else
        {
            stack.Peek().Block.Children.Add(closed);
        }
    }

    private static void OpenBlock(
        IniParseContext context,
        string line,
        int indent,
        int lineNumber,
        string? comment)
    {
        var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        ReportAmbiguousNesting(context, lineNumber, tokens[0]);
        var block = new IniBlock
        {
            BlockType = tokens[0],
            Name = tokens.Length > 1 ? string.Join(' ', tokens[1..]) : string.Empty,
            TrailingComment = comment,
            LineNumber = lineNumber,
        };
        block.LeadingComments.AddRange(context.PendingComments);
        context.PendingComments.Clear();
        if (context.Stack.Count == 0 && context.Document.Blocks.Count == 0)
        {
            DrainDocumentHeader(context.Document, block);
        }

        context.Stack.Push(new BlockFrame(block, indent));
    }

    /// <summary>
    /// Flags a block header that repeats an ancestor's type. Same-type blocks never
    /// nest legitimately, so this shape means a sibling boundary was lost to a
    /// missing <c>End</c> and serializing would bake in the wrong nesting.
    /// </summary>
    /// <param name="context">The parsing context.</param>
    /// <param name="lineNumber">The 1-based line number of the new header.</param>
    /// <param name="blockType">The block type being opened.</param>
    private static void ReportAmbiguousNesting(
        IniParseContext context,
        int lineNumber,
        string blockType)
    {
        var conflictingFrame = context.Stack.FirstOrDefault(frame =>
            string.Equals(frame.Block.BlockType, blockType, StringComparison.OrdinalIgnoreCase));
        if (conflictingFrame is not null)
        {
            context.Errors.Add($"Line {lineNumber}: Block '{blockType}' opens inside unclosed '{conflictingFrame.Block.DisplayHeader}' (missing 'End'?).");
            context.Document.HasDiscardedContent = true;
        }
    }

    private static void DrainDocumentHeader(IniDocument document, IniBlock block)
    {
        if (block.LeadingComments.Count == 0)
        {
            return;
        }

        document.HeaderComments.AddRange(block.LeadingComments);
        block.LeadingComments.Clear();
    }

    private static void FlushTrailingComments(IniDocument document, Stack<BlockFrame> stack, List<IniComment> pendingComments)
    {
        if (pendingComments.Count == 0)
        {
            return;
        }

        if (stack.Count == 0)
        {
            document.TrailingComments.AddRange(pendingComments);
        }
        else
        {
            stack.Peek().Block.TrailingComments.AddRange(pendingComments);
        }

        pendingComments.Clear();
    }

    private static void CloseUnclosedBlocks(IniDocument document, Stack<BlockFrame> stack, List<string> errors)
    {
        while (stack.Count > 0)
        {
            var open = stack.Pop().Block;
            errors.Add($"Line {open.LineNumber}: Block '{open.BlockType} {open.Name}' is missing 'End'.".Trim());
            if (stack.Count == 0)
            {
                document.Blocks.Add(open);
            }
            else
            {
                stack.Peek().Block.Children.Add(open);
            }
        }
    }

    private static void ValidateSiblingBlocks(List<IniBlock> siblings, string targetId, List<ValidationIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in siblings)
        {
            ValidateBlock(block, targetId, issues, names);
        }
    }

    private static void ValidateBlock(IniBlock block, string targetId, List<ValidationIssue> issues, HashSet<string> names)
    {
        if (block.BlockType.Length == 0)
        {
            issues.Add(new ValidationIssue(
                $"Block at line {block.LineNumber} is missing a block type.",
                ValidationSeverity.Error,
                targetId));
        }

        if (!names.Add(block.DisplayHeader))
        {
            issues.Add(new ValidationIssue(
                $"Duplicate block '{block.DisplayHeader}'.",
                ValidationSeverity.Warning,
                targetId));
        }

        if (block.Fields.Count == 0 && block.Children.Count == 0)
        {
            issues.Add(new ValidationIssue(
                $"Block '{block.DisplayHeader}' is empty.",
                ValidationSeverity.Warning,
                targetId));
        }

        ValidateSiblingBlocks(block.Children, targetId, issues);
    }

    private static void WriteBlock(StringBuilder builder, IniBlock block, int indent)
    {
        WriteComments(builder, block.LeadingComments, indent);
        AppendLine(builder, indent, AppendTrailingComment(block.DisplayHeader, block.TrailingComment));
        foreach (var field in block.Fields)
        {
            WriteComments(builder, field.LeadingComments, indent + 1);
            AppendLine(builder, indent + 1, AppendTrailingComment(FormatFieldText(field), field.TrailingComment));
        }

        foreach (var child in block.Children)
        {
            WriteBlock(builder, child, indent + 1);
        }

        WriteComments(builder, block.TrailingComments, indent + 1);
        AppendLine(builder, indent, IniConstants.BlockTags.End);
    }

    private static void WriteComments(StringBuilder builder, List<IniComment> comments, int indent)
    {
        foreach (var comment in comments)
        {
            if (comment.IsDirective)
            {
                AppendLine(builder, indent, comment.Text);
            }
            else
            {
                AppendLine(builder, indent, comment.Text.Length == 0 ? ";" : $"; {comment.Text}");
            }
        }
    }

    private static string AppendTrailingComment(string code, string? comment)
    {
        return comment == null ? code : $"{code} ; {comment}";
    }

    private static string FormatFieldText(IniField field)
    {
        if (!field.IsBare &&
            !string.Equals(field.Key, IniConstants.MapDirectives.RemoveModule, StringComparison.OrdinalIgnoreCase))
        {
            return $"{field.Key} = {field.Value}";
        }

        return string.IsNullOrEmpty(field.Value) ? field.Key : $"{field.Key} {field.Value}";
    }

    private static void AppendLine(StringBuilder builder, int indent, string text)
    {
        builder.Append(' ', indent * 2);
        builder.Append(text);
        builder.Append(IniConstants.Syntax.NewLine);
    }

    private static (string Code, string? Comment) SplitComment(string raw)
    {
        var inQuotes = false;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '"')
            {
                inQuotes = !inQuotes;
            }

            if (!inQuotes && raw[i] == IniConstants.Syntax.Comment)
            {
                return (raw[..i], raw[(i + 1)..].Trim());
            }
        }

        return (raw, null);
    }

    private static byte[] StripUtf8Bom(byte[] bytes)
    {
        if (bytes.Length >= Utf8Bom.Length &&
            bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
        {
            return bytes[Utf8Bom.Length..];
        }

        return bytes;
    }

    private (string Content, Encoding Encoding) DecodeContent(byte[] bytes, string filePath)
    {
        var contentBytes = StripUtf8Bom(bytes);
        var hasBom = contentBytes.Length != bytes.Length;
        try
        {
            var encoding = new UTF8Encoding(hasBom, throwOnInvalidBytes: true);
            return (encoding.GetString(contentBytes).TrimStart('\uFEFF'), encoding);
        }
        catch (DecoderFallbackException ex)
        {
            logger.LogWarning(ex, "File {Path} is not valid UTF-8; decoding as single byte ANSI text", filePath);
            return (Encoding.Latin1.GetString(contentBytes), Encoding.Latin1);
        }
    }
}
