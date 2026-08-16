using GenHub.Core.Interfaces.Tools.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for processing text files with various transformations.
/// </summary>
public sealed class TextProcessingService(
    ILogger<TextProcessingService> logger) : ITextProcessingService
{
    /// <inheritdoc />
    public async Task<string> ProcessTextAsync(
        string content,
        TextProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = content;

        // Apply transformations in order
        if (options.DeleteComments)
        {
            result = await RemoveCommentsAsync(result, options.CommentStyle, cancellationToken)
                .ConfigureAwait(false);
        }

        if (options.DeleteWhitespace)
        {
            result = await RemoveWhitespaceAsync(result, options.WhitespaceMode, cancellationToken)
                .ConfigureAwait(false);
        }

        if (options.ForceEOL.HasValue)
        {
            result = await NormalizeLineEndingsAsync(result, options.ForceEOL.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<string> NormalizeLineEndingsAsync(
        string content,
        LineEndingType type,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = type switch
        {
            LineEndingType.CRLF => content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"),
            LineEndingType.LF => content.Replace("\r\n", "\n").Replace("\r", "\n"),
            LineEndingType.CR => content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r"),
            _ => content,
        };

        logger.LogDebug("Normalized line endings to {Type}", type);
        return Task.FromResult(normalized);
    }

    /// <inheritdoc />
    public Task<string> RemoveCommentsAsync(
        string content,
        CommentStyle style,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(content))
        {
            return Task.FromResult(content);
        }

        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var result = new StringBuilder(content.Length);

        var commentPrefix = style switch
        {
            CommentStyle.IniStyle => ";",
            CommentStyle.CStyle => "//",
            CommentStyle.ScriptStyle => "#",
            _ => ";",
        };

        var removedCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Skip lines that start with comment
            if (trimmed.StartsWith(commentPrefix, StringComparison.Ordinal))
            {
                removedCount++;
                continue;
            }

            // Remove inline comments
            var commentIndex = line.IndexOf(commentPrefix, StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                result.Append(line.Substring(0, commentIndex).TrimEnd());
                removedCount++;
            }
            else
            {
                result.Append(line);
            }

            if (i < lines.Length - 1)
            {
                result.Append('\n');
            }
        }

        logger.LogDebug("Removed {Count} comments with style {Style}", removedCount, style);
        return Task.FromResult(result.ToString());
    }

    /// <inheritdoc />
    public Task<string> RemoveWhitespaceAsync(
        string content,
        WhitespaceMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(content))
        {
            return Task.FromResult(content);
        }

        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var result = new StringBuilder(content.Length);
        var removedLines = 0;
        var processedLines = new List<string>();

        foreach (var line in lines)
        {
            var processed = mode switch
            {
                WhitespaceMode.Leading => line.TrimStart(),
                WhitespaceMode.Trailing => line.TrimEnd(),
                WhitespaceMode.EmptyLines => string.IsNullOrWhiteSpace(line) ? null : line,
                WhitespaceMode.ExtraOnly => Regex.Replace(line, @"\s+", " "),
                WhitespaceMode.All => line.Trim(),
                _ => line,
            };

            if (processed != null)
            {
                processedLines.Add(processed);
            }
            else
            {
                removedLines++;
            }
        }

        for (int i = 0; i < processedLines.Count; i++)
        {
            result.Append(processedLines[i]);
            if (i < processedLines.Count - 1)
            {
                result.Append('\n');
            }
        }

        logger.LogDebug("Processed whitespace with mode {Mode}, removed {Count} empty lines", mode, removedLines);
        return Task.FromResult(result.ToString());
    }

    /// <inheritdoc />
    public async Task<string> OptimizeIniFileAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Optimizing INI file content");

        // Combine all optimizations for INI files
        var options = new TextProcessingOptions
        {
            DeleteComments = true,
            CommentStyle = CommentStyle.IniStyle,
            ForceEOL = LineEndingType.CRLF,
            DeleteWhitespace = true,
            WhitespaceMode = WhitespaceMode.ExtraOnly,
        };

        var result = await ProcessTextAsync(content, options, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("INI file optimization complete");
        return result;
    }
}
