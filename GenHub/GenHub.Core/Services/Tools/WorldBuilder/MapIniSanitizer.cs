namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Line-preserving map.ini check. Mirrors the editor sanitize pass:
/// module tags introduced per Object block are tracked, duplicate
/// RemoveModule lines are neutralized to comments, and unverifiable lines
/// are kept with warnings (template data needs game data).
/// </summary>
public static class MapIniSanitizer
{
    private static readonly HashSet<string> ModuleHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Behavior", "Draw", "Body", "ClientUpdate", "ClientBehavior",
    };

    /// <summary>
    /// Sanitizes map.ini text.
    /// </summary>
    /// <param name="text">The original text.</param>
    /// <returns>Sanitized text plus warnings.</returns>
    public static (string Text, IReadOnlyList<string> Warnings) Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var warnings = new List<string>();
        var output = new List<string>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentObject = string.Empty;
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            output.Add(ProcessLine(lines[i], i + 1, ref currentObject, added, removed, warnings));
        }

        return (string.Join("\n", output), warnings);
    }

    private static string ProcessLine(
        string line,
        int lineNumber,
        ref string currentObject,
        HashSet<string> added,
        HashSet<string> removed,
        List<string> warnings)
    {
        var work = StripComments(line);
        var tokens = work.Split([' ', '\t', '\r', '='], StringSplitOptions.RemoveEmptyEntries);
        var first = tokens.Length > 0 ? tokens[0] : null;
        var second = tokens.Length > 1 ? tokens[1] : null;
        var third = tokens.Length > 2 ? tokens[2] : null;
        if (first == "Object" && second != null && !work.Contains('='))
        {
            currentObject = second;
            added.Clear();
            removed.Clear();
            return line;
        }

        if (first == "RemoveModule" && second != null)
        {
            return ProcessRemoveModule(line, lineNumber, currentObject, second, added, removed, warnings);
        }

        if (first != null && ModuleHeaders.Contains(first))
        {
            TrackModuleTags(second, third, added);
        }

        return line;
    }

    private static string StripComments(string line)
    {
        var work = line;
        var comment = work.IndexOf(';');
        if (comment >= 0)
        {
            work = work[..comment];
        }

        var slashes = work.IndexOf("//", StringComparison.Ordinal);
        if (slashes >= 0)
        {
            work = work[..slashes];
        }

        return work;
    }

    private static string ProcessRemoveModule(
        string line,
        int lineNumber,
        string currentObject,
        string module,
        HashSet<string> added,
        HashSet<string> removed,
        List<string> warnings)
    {
        if (removed.Contains(module))
        {
            warnings.Add($"line {lineNumber}: RemoveModule {module} -- already removed in '{currentObject}'");
            return $"; sanitized: {line}";
        }

        if (added.Contains(module))
        {
            removed.Add(module);
            return line;
        }

        warnings.Add($"line {lineNumber}: RemoveModule {module} -- unverifiable without game data in '{currentObject}'");
        return line;
    }

    private static void TrackModuleTags(string? second, string? third, HashSet<string> added)
    {
        if (second != null)
        {
            added.Add(second);
        }

        if (third != null)
        {
            added.Add(third);
        }
    }
}
