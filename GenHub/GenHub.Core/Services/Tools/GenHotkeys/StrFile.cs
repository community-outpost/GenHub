// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Text;

namespace GenHub.Core.Services.Tools.GenHotkeys;

/// <summary>
/// Westwood/EA C&amp;C Generals text string table (.str) parser: the editable source
/// behind the compiled .csf binary. Mirrors GameTextManager::parseStringFile and
/// readToEndOfQuote: each entry is a bare label line, followed by a single
/// double-quoted value that may span physical lines (each embedded newline reads
/// as one space), terminated by an End line. Backslashes are preserved verbatim:
/// a backslash only stops the next quote from closing the value. A second quoted
/// value for the same label is ignored, matching release-engine behavior.
/// Lookup is case-insensitive like the engine. Structurally malformed input
/// (value without a label, End inside a value, a new label before End, missing
/// End) throws, which is stricter than the engine's silent skips and protects
/// map authors from silently lost strings.
/// </summary>
public sealed class StrFile
{
    private sealed class StrParser
    {
        public StrFile File { get; } = new();

        public string Label { get; set; } = string.Empty;

        public string? Value { get; set; }

        public StringBuilder? Pending { get; set; }

        public bool KeepPending { get; set; }

        public int LineNumber { get; set; }

        public void FinishValue()
        {
            if (KeepPending && Pending is not null)
            {
                Value = Pending.ToString().Trim();
            }

            Pending = null;
        }
    }

    private const string EndToken = "End";
    private const char ValueQuote = '"';

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets all label-value pairs.</summary>
    public IReadOnlyDictionary<string, string> Strings => _strings;

    /// <summary>Gets the count of loaded strings.</summary>
    public int Count => _strings.Count;

    /// <summary>
    /// Loads a STR file from the specified path.
    /// </summary>
    /// <param name="filePath">Absolute path to the .str file.</param>
    /// <returns>A loaded <see cref="StrFile"/> instance.</returns>
    public static StrFile Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>
    /// Loads a STR file from a stream.
    /// </summary>
    /// <param name="stream">The readable stream containing STR text.</param>
    /// <returns>A loaded <see cref="StrFile"/> instance.</returns>
    public static StrFile Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return LoadText(reader.ReadToEnd());
    }

    /// <summary>
    /// Loads a STR file from text.
    /// </summary>
    /// <param name="text">The STR file text.</param>
    /// <returns>A loaded <see cref="StrFile"/> instance.</returns>
    public static StrFile LoadText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new StrParser();
        foreach (var rawLine in text.Split('\n'))
        {
            parser.LineNumber++;
            ParseLine(parser, rawLine.TrimEnd('\r').Trim());
        }

        ValidateComplete(parser);
        return parser.File;
    }

    /// <summary>
    /// Gets the string value by its label.
    /// </summary>
    /// <param name="label">STR label name.</param>
    /// <returns>The string value or empty string if not found.</returns>
    public string GetString(string label)
    {
        return _strings.TryGetValue(label, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// Sets or adds a label and value pair in the string table.
    /// </summary>
    /// <param name="label">STR label name.</param>
    /// <param name="value">String value to assign.</param>
    public void SetString(string label, string value)
    {
        _strings[label] = value;
    }

    private static void ParseLine(StrParser parser, string line)
    {
        if (parser.Pending is not null)
        {
            ParseContinuation(parser, line);
            return;
        }

        if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        if (line.Equals(EndToken, StringComparison.OrdinalIgnoreCase))
        {
            FinishEntry(parser);
            return;
        }

        if (line.StartsWith(ValueQuote))
        {
            StartValue(parser, line);
            return;
        }

        StartLabel(parser, line);
    }

    private static void ParseContinuation(StrParser parser, string line)
    {
        if (line.Equals(EndToken, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Line {parser.LineNumber}: End inside label '{parser.Label}' value.");
        }

        parser.Pending!.Append(' ');
        if (ConsumeQuotedContent(parser.Pending, line, 0))
        {
            parser.FinishValue();
        }
    }

    private static void FinishEntry(StrParser parser)
    {
        if (parser.Label.Length == 0)
        {
            throw new InvalidDataException($"Line {parser.LineNumber}: End without a label.");
        }

        parser.File._strings[parser.Label] = parser.Value ?? string.Empty;
        parser.Label = string.Empty;
        parser.Value = null;
    }

    private static void StartValue(StrParser parser, string line)
    {
        if (parser.Label.Length == 0)
        {
            throw new InvalidDataException($"Line {parser.LineNumber}: value without a label.");
        }

        parser.Pending = new StringBuilder(line.Length);
        parser.KeepPending = parser.Value is null;
        if (ConsumeQuotedContent(parser.Pending, line, 1))
        {
            parser.FinishValue();
        }
    }

    private static void StartLabel(StrParser parser, string line)
    {
        if (parser.Label.Length != 0)
        {
            throw new InvalidDataException($"Line {parser.LineNumber}: label '{line}' starts before '{parser.Label}' ends.");
        }

        parser.Label = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static void ValidateComplete(StrParser parser)
    {
        if (parser.Pending is not null)
        {
            throw new InvalidDataException($"Label '{parser.Label}' has an unterminated value.");
        }

        if (parser.Label.Length != 0)
        {
            throw new InvalidDataException($"Label '{parser.Label}' is missing its End token.");
        }
    }

    /// <summary>
    /// Appends quoted content mirroring the engine slash-flag state machine: a
    /// backslash is preserved verbatim and only stops the next quote from closing
    /// the value, and tabs fold to spaces. Returns true at the first unescaped
    /// closing quote; the rest of the line is ignored like the engine ignores it.
    /// </summary>
    private static bool ConsumeQuotedContent(StringBuilder builder, string line, int startIndex)
    {
        var slash = false;
        var index = startIndex;
        while (index < line.Length)
        {
            var current = line[index];
            if (current == '\\')
            {
                slash = !slash;
                builder.Append(current);
            }
            else if (current == ValueQuote && !slash)
            {
                return true;
            }
            else
            {
                slash = false;
                builder.Append(current == '\t' ? ' ' : current);
            }

            index++;
        }

        return false;
    }
}
