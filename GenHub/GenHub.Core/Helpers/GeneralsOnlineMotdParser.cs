using GenHub.Core.Constants;
using GenHub.Core.Models.GeneralsOnline;
using System.Collections.Generic;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Parses Generals Online message-of-the-day color codes. The backend embeds
/// inline colors as a backslash followed by eight ARGB hex digits (for example
/// \ffff7f2f for orange headers); the alpha channel is always opaque, so only
/// the RGB part is surfaced.
/// </summary>
public static class GeneralsOnlineMotdParser
{
    /// <summary>
    /// Splits MOTD text into color runs, one per inline color segment.
    /// Unknown or truncated codes are kept as literal text.
    /// </summary>
    /// <param name="motd">The raw MOTD text.</param>
    /// <returns>The color runs in display order.</returns>
    public static IReadOnlyList<GeneralsOnlineMotdRun> Parse(string? motd)
    {
        var runs = new List<GeneralsOnlineMotdRun>();
        if (string.IsNullOrEmpty(motd))
        {
            return runs;
        }

        var pending = new StringBuilder(motd.Length);
        string? pendingColor = null;
        var index = 0;
        while (index < motd.Length)
        {
            if (TryReadColorCode(motd, index, out var color))
            {
                if (!string.Equals(color, pendingColor, StringComparison.Ordinal))
                {
                    FlushRun(runs, pending, pendingColor);
                    pendingColor = color;
                }

                index += GeneralsOnlineConstants.MotdColorCodeLength;
            }
            else
            {
                pending.Append(motd[index]);
                index++;
            }
        }

        FlushRun(runs, pending, pendingColor);
        return runs;
    }

    /// <summary>
    /// Removes MOTD color codes, keeping the display text.
    /// </summary>
    /// <param name="motd">The raw MOTD text.</param>
    /// <returns>The text without color codes.</returns>
    public static string StripColorCodes(string? motd)
    {
        if (string.IsNullOrEmpty(motd))
        {
            return string.Empty;
        }

        var stripped = new StringBuilder(motd.Length);
        var index = 0;
        while (index < motd.Length)
        {
            if (TryReadColorCode(motd, index, out _))
            {
                index += GeneralsOnlineConstants.MotdColorCodeLength;
            }
            else
            {
                stripped.Append(motd[index]);
                index++;
            }
        }

        return stripped.ToString();
    }

    private static void FlushRun(List<GeneralsOnlineMotdRun> runs, StringBuilder pending, string? color)
    {
        if (pending.Length == 0)
        {
            return;
        }

        runs.Add(new GeneralsOnlineMotdRun(pending.ToString(), color));
        pending.Clear();
    }

    private static bool IsHexDigit(char value)
    {
        return (value >= '0' && value <= '9')
            || (value >= 'a' && value <= 'f')
            || (value >= 'A' && value <= 'F');
    }

    private static bool TryReadColorCode(string motd, int index, out string? color)
    {
        color = null;
        if (motd[index] != GeneralsOnlineConstants.MotdColorCodePrefix)
        {
            return false;
        }

        if (index + GeneralsOnlineConstants.MotdColorCodeLength > motd.Length)
        {
            return false;
        }

        for (var offset = 1; offset < GeneralsOnlineConstants.MotdColorCodeLength; offset++)
        {
            if (!IsHexDigit(motd[index + offset]))
            {
                return false;
            }
        }

        var rgbOffset = GeneralsOnlineConstants.MotdColorCodeLength - GeneralsOnlineConstants.MotdColorRgbHexDigits;
        color = GeneralsOnlineConstants.MotdColorHexPrefix + motd.Substring(index + rgbOffset, GeneralsOnlineConstants.MotdColorRgbHexDigits).ToUpperInvariant();
        return true;
    }
}
