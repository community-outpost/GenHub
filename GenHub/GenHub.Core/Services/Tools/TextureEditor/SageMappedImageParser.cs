using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GenHub.Core.Services.Tools.TextureEditor;

/// <summary>
/// Parses and serializes SAGE MappedImage INI blocks.
/// </summary>
public sealed class SageMappedImageParser(ILogger<SageMappedImageParser> logger) : ISageMappedImageParser
{
    private static readonly Regex CoordsPattern = new(
        @"\b(Left|Top|Right|Bottom)\s*[:=]\s*(-?\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TextureEditorConstants.CoordsRegexTimeout);

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<MappedImageDefinition>> ParseText(string content, string? sourcePath = null)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(content);

        var images = new List<MappedImageDefinition>();
        var errors = new List<string>();
        var lines = content.ReplaceLineEndings("\n").Split('\n');

        MappedImageBlock? current = null;
        int lineNumber = 0;
        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            current = ProcessContentLine(line, lineNumber, current, sourcePath, images, errors);
        }

        if (current is not null)
        {
            errors.Add(FormatError(sourcePath, current.StartLine, $"Missing '{TextureEditorConstants.IniBlockEnd}' for '{current.Name}'."));
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        if (errors.Count > 0)
        {
            logger.LogWarning("Parsed {Count} mapped images with {Errors} malformed blocks from {Source}", images.Count, errors.Count, sourcePath ?? "text");
            return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateFailure(errors, images, elapsed);
        }

        return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateSuccess(images, elapsed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<MappedImageDefinition>>> ParseFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateFailure($"MappedImages INI file not found: {path}", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return ParseText(content, path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read MappedImages INI file: {Path}", path);
            return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateFailure($"Failed to read MappedImages INI file: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied reading MappedImages INI file: {Path}", path);
            return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateFailure($"Access denied reading MappedImages INI file: {path}", Stopwatch.GetElapsedTime(started));
        }
    }

    /// <inheritdoc />
    public string Serialize(IEnumerable<MappedImageDefinition> images, string? headerComment = null)
    {
        ArgumentNullException.ThrowIfNull(images);

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(headerComment))
        {
            foreach (var headerLine in headerComment.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append("; ").AppendLine(headerLine.Trim());
            }

            sb.AppendLine();
        }

        foreach (var image in images)
        {
            ValidateSerializeField(image.Name, nameof(image.Name));
            ValidateSerializeField(image.TextureFileName, nameof(image.TextureFileName));
            ValidateSerializeField(image.Status, nameof(image.Status));
            sb.Append(TextureEditorConstants.IniBlockName).Append(' ').AppendLine(image.Name);
            sb.Append("  Texture = ").AppendLine(image.TextureFileName);
            sb.Append("  TextureWidth = ").AppendLine(image.TextureWidth.ToString());
            sb.Append("  TextureHeight = ").AppendLine(image.TextureHeight.ToString());
            sb.Append("  Coords = Left:").Append(image.Left).Append(" Top:").Append(image.Top)
                .Append(" Right:").Append(image.Right).Append(" Bottom:").AppendLine(image.Bottom.ToString());
            sb.Append("  Status = ").AppendLine(image.Status);
            sb.AppendLine(TextureEditorConstants.IniBlockEnd);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static MappedImageBlock? ProcessContentLine(string line, int lineNumber, MappedImageBlock? current, string? sourcePath, List<MappedImageDefinition> images, List<string> errors)
    {
        if (IsBlockStart(line))
        {
            if (current is not null)
            {
                errors.Add(FormatError(sourcePath, current.StartLine, $"Missing '{TextureEditorConstants.IniBlockEnd}' for '{current.Name}'."));
            }

            return MappedImageBlock.Start(ParseBlockName(line), lineNumber);
        }

        if (IsBlockEnd(line))
        {
            return FinishBlock(lineNumber, current, sourcePath, images, errors);
        }

        current?.Apply(line);
        return current;
    }

    private static MappedImageBlock? FinishBlock(int lineNumber, MappedImageBlock? current, string? sourcePath, List<MappedImageDefinition> images, List<string> errors)
    {
        if (current is null)
        {
            errors.Add(FormatError(sourcePath, lineNumber, $"Stray '{TextureEditorConstants.IniBlockEnd}' without a MappedImage block."));
            return null;
        }

        var built = current.Build(sourcePath);
        if (built is null)
        {
            errors.Add(FormatError(sourcePath, current.StartLine, $"Incomplete MappedImage block '{current.Name}'."));
        }
        else
        {
            images.Add(built);
        }

        return null;
    }

    private static bool IsBlockStart(string line) =>
        line.StartsWith(TextureEditorConstants.IniBlockName, StringComparison.OrdinalIgnoreCase) &&
        (line.Length == TextureEditorConstants.IniBlockName.Length ||
            char.IsWhiteSpace(line[TextureEditorConstants.IniBlockName.Length]));

    private static bool IsBlockEnd(string line) =>
        line.Equals(TextureEditorConstants.IniBlockEnd, StringComparison.OrdinalIgnoreCase);

    private static string ParseBlockName(string line) =>
        line.Substring(TextureEditorConstants.IniBlockName.Length).Trim();

    private static string StripComment(string line)
    {
        int index = line.IndexOf(';');
        return index < 0 ? line : line.Substring(0, index);
    }

    private static string FormatError(string? sourcePath, int lineNumber, string message) =>
        sourcePath is null ? $"Line {lineNumber}: {message}" : $"{sourcePath} line {lineNumber}: {message}";

    private static void ValidateSerializeField(string value, string fieldName)
    {
        // SAGE INI has no escape mechanism: line breaks would inject extra lines
        // and ';' would be stripped as a comment on reload, so reject them.
        if (value.Contains('\r') || value.Contains('\n') || value.Contains(';'))
        {
            throw new ArgumentException($"MappedImage {fieldName} must not contain line breaks or ';'.", fieldName);
        }
    }

    private sealed class MappedImageBlock
    {
        private MappedImageBlock(string name, int startLine)
        {
            Name = name;
            StartLine = startLine;
        }

        public string Name { get; }

        public int StartLine { get; }

        private string? Texture { get; set; }

        private int TextureWidth { get; set; }

        private int TextureHeight { get; set; }

        private int Left { get; set; }

        private int Top { get; set; }

        private int Right { get; set; }

        private int Bottom { get; set; }

        private bool HasCoords { get; set; }

        private string Status { get; set; } = TextureEditorConstants.DefaultStatus;

        public static MappedImageBlock Start(string name, int startLine) => new(name, startLine);

        public void Apply(string line)
        {
            int separator = line.IndexOf('=');
            if (separator < 0)
            {
                return;
            }

            var key = line.Substring(0, separator).Trim();
            var value = line.Substring(separator + 1).Trim();
            if (key.Equals("Texture", StringComparison.OrdinalIgnoreCase))
            {
                Texture = value;
            }
            else if (key.Equals("TextureWidth", StringComparison.OrdinalIgnoreCase))
            {
                TextureWidth = ParseInt(value);
            }
            else if (key.Equals("TextureHeight", StringComparison.OrdinalIgnoreCase))
            {
                TextureHeight = ParseInt(value);
            }
            else if (key.Equals("Coords", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCoords(value);
            }
            else if (key.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                Status = value.Length == 0 ? TextureEditorConstants.DefaultStatus : value;
            }
        }

        public MappedImageDefinition? Build(string? sourcePath)
        {
            if (Name.Length == 0 || string.IsNullOrWhiteSpace(Texture) || !HasCoords)
            {
                return null;
            }

            return new MappedImageDefinition(Name, Texture, TextureWidth, TextureHeight, Left, Top, Right, Bottom, Status, sourcePath);
        }

        private static int ParseInt(string value) =>
            int.TryParse(value, out int parsed) ? parsed : 0;

        private void ApplyCoords(string value)
        {
            MatchCollection matches;
            try
            {
                matches = CoordsPattern.Matches(value);
            }
            catch (RegexMatchTimeoutException)
            {
                return;
            }

            int? left = null;
            int? top = null;
            int? right = null;
            int? bottom = null;
            foreach (var groups in matches.Cast<Match>().Select(match => match.Groups))
            {
                if (!int.TryParse(groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    continue;
                }

                switch (groups[1].Value.ToUpperInvariant())
                {
                    case "LEFT":
                        left = number;
                        break;
                    case "TOP":
                        top = number;
                        break;
                    case "RIGHT":
                        right = number;
                        break;
                    case "BOTTOM":
                        bottom = number;
                        break;
                    default:
                        break;
                }
            }

            // Sonar S2583 false positive: the engine models the Matches loop as never
            // assigning, but the mixed-case and unordered Coords parser tests prove all
            // four keys assign. The guard is required to reject incomplete Coords lines.
#pragma warning disable S2583
            if (left is null || top is null || right is null || bottom is null)
            {
                return;
            }
#pragma warning restore S2583

            Left = left.Value;
            Top = top.Value;
            Right = right.Value;
            Bottom = bottom.Value;
            HasCoords = true;
        }
    }
}
