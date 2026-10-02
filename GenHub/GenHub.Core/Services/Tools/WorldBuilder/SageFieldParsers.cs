// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using System.Globalization;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Shared raw-field readers for the WorldBuilder catalog services: first-value and
/// all-value lookup plus the scalar parsers mirroring INI::scanBool, scanInt,
/// scanReal, and parseColorInt. Unparsable values yield null so one bad field never
/// fails a whole catalog.
/// </summary>
internal static class SageFieldParsers
{
    /// <summary>
    /// Gets the first value of the first block field with the key. A leading quote
    /// joins the remaining values with spaces like getNextAsciiString.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="key">The field key (case-sensitive).</param>
    /// <returns>The first value, or null when absent.</returns>
    internal static string? FirstValue(SageIniBlock block, string key)
    {
        ArgumentNullException.ThrowIfNull(block);
        foreach (var field in block.Fields)
        {
            if (field.Key.Equals(key, StringComparison.Ordinal) && field.Values.Count > 0)
            {
                return CombineQuoted(field.Values);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the first value of the first sub-block field with the key. A leading quote
    /// joins the remaining values with spaces like getNextAsciiString.
    /// </summary>
    /// <param name="subBlock">The sub-block.</param>
    /// <param name="key">The field key (case-sensitive).</param>
    /// <returns>The first value, or null when absent.</returns>
    internal static string? FirstValue(SageIniSubBlock subBlock, string key)
    {
        ArgumentNullException.ThrowIfNull(subBlock);
        foreach (var field in subBlock.Fields)
        {
            if (field.Key.Equals(key, StringComparison.Ordinal) && field.Values.Count > 0)
            {
                return CombineQuoted(field.Values);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets every value of every block field with the key, in source order.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="key">The field key (case-sensitive).</param>
    /// <returns>The concatenated values.</returns>
    internal static IReadOnlyList<string> AllValues(SageIniBlock block, string key)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Fields
            .Where(field => field.Key.Equals(key, StringComparison.Ordinal))
            .SelectMany(field => field.Values)
            .ToList();
    }

    /// <summary>
    /// Gets each block field line with the key as one space-joined string, in source order.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="key">The field key (case-sensitive).</param>
    /// <returns>The joined lines.</returns>
    internal static IReadOnlyList<string> JoinedLines(SageIniBlock block, string key)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Fields
            .Where(field => field.Key.Equals(key, StringComparison.Ordinal))
            .Select(field => string.Join(' ', field.Values))
            .ToList();
    }

    /// <summary>
    /// Parses an engine boolean (Yes/No plus common true/false spellings).
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The boolean, or null when missing or unrecognized.</returns>
    internal static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("0", StringComparison.Ordinal)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// Parses an invariant-culture integer.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The integer, or null when missing or unparsable.</returns>
    internal static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Parses an invariant-culture float.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The float, or null when missing or unparsable.</returns>
    internal static float? ParseFloat(string? value)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Parses an engine percent (trailing % divides by 100, like parsePercentToReal).
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The fraction, or null when missing or unparsable.</returns>
    internal static float? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith('%'))
        {
            var percent = ParseFloat(trimmed[..^1]);
            return percent is null ? null : percent / 100.0f;
        }

        return ParseFloat(trimmed);
    }

    /// <summary>
    /// Parses an engine color (R:r G:g B:b with optional A:a, or bare channels) into ARGB.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="key">The field key (case-sensitive).</param>
    /// <returns>The ARGB integer, or null when fewer than three channels parse.</returns>
    internal static int? ParseColor(SageIniBlock block, string key)
    {
        ArgumentNullException.ThrowIfNull(block);
        var channels = new List<int>(4);
        foreach (var field in block.Fields)
        {
            if (!field.Key.Equals(key, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var token in field.Values)
            {
                var number = StripChannelPrefix(token);
                if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel))
                {
                    channels.Add(Math.Clamp(channel, 0, 255));
                }
            }
        }

        if (channels.Count < 3)
        {
            return null;
        }

        var alpha = channels.Count > 3 ? channels[3] : 255;
        return (alpha << 24) | (channels[0] << 16) | (channels[1] << 8) | channels[2];
    }

    private static string CombineQuoted(IReadOnlyList<string> values)
    {
        if (!values[0].StartsWith('"'))
        {
            return values[0];
        }

        var joined = string.Join(' ', values);
        var stripped = joined.Length > 1 ? joined[1..] : string.Empty;
        return stripped.Length > 0 && stripped.EndsWith('"') ? stripped[..^1] : stripped;
    }

    private static string StripChannelPrefix(string token)
    {
        var separator = token.IndexOf(':');
        if (separator >= 0 && separator + 1 < token.Length)
        {
            return token[(separator + 1)..];
        }

        if (token.Length == 1 && char.IsLetter(token[0]))
        {
            return string.Empty;
        }

        return token;
    }
}
