using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Service for parsing map display names and player counts from .map files and directories.
/// </summary>
public partial class MapNameParser(ILogger<MapNameParser> logger)
{
    private const string PlayersGroupName = "players";
    private const string SlotGroupName = "slot";

    /// <summary>
    /// Extracts the player count from a string (display name, filename, or directory name) using common naming patterns.
    /// </summary>
    /// <param name="text">The string to analyze.</param>
    /// <returns>The player count if matched, otherwise null.</returns>
    public static int? ExtractPlayerCountFromString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Priority 1: Parenthesized or bracketed number, e.g. "(2) Tournament Desert", "Twilight Flame [4]"
        var bracketMatch = BracketedPlayerCountRegex().Match(text);
        if (bracketMatch.Success && int.TryParse(bracketMatch.Groups[PlayersGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bracketCount) && bracketCount is >= 1 and <= 8)
        {
            return bracketCount;
        }

        // Priority 2: Keyword "players" or "player", e.g. "8 Players", "4 Player"
        var keywordMatch = KeywordPlayerCountRegex().Match(text);
        if (keywordMatch.Success && int.TryParse(keywordMatch.Groups[PlayersGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keywordCount) && keywordCount is >= 1 and <= 8)
        {
            return keywordCount;
        }

        // Priority 3: Hyphenated "2-player" or "4-player"
        var hyphenMatch = HyphenatedPlayerCountRegex().Match(text);
        if (hyphenMatch.Success && int.TryParse(hyphenMatch.Groups[PlayersGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hyphenCount) && hyphenCount is >= 1 and <= 8)
        {
            return hyphenCount;
        }

        // Priority 4: Delimited player count "4p" or "_2p"
        var pMatch = ShortPPlayerCountRegex().Match(text);
        if (pMatch.Success && int.TryParse(pMatch.Groups[PlayersGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pCount) && pCount is >= 1 and <= 8)
        {
            return pCount;
        }

        return null;
    }

    /// <summary>
    /// Parses the display name for a map from its file path.
    /// </summary>
    /// <param name="mapFilePath">Path to the .map file.</param>
    /// <returns>The parsed display name.</returns>
    public string ParseMapName(string mapFilePath)
    {
        var nameFromFile = TryParseFromMapFile(mapFilePath);
        if (!string.IsNullOrWhiteSpace(nameFromFile))
        {
            return nameFromFile;
        }

        var nameFromDirectory = FallbackToDirectoryName(mapFilePath);
        if (!string.IsNullOrWhiteSpace(nameFromDirectory))
        {
            return nameFromDirectory;
        }

        return CleanMapName(Path.GetFileNameWithoutExtension(mapFilePath));
    }

    /// <summary>
    /// Parses the number of players supported by the map from its file contents, directory name, or filename.
    /// </summary>
    /// <param name="mapFilePath">Path to the .map file.</param>
    /// <param name="displayName">Optional parsed display name for heuristic fallback.</param>
    /// <returns>The parsed number of players, or null if undetermined.</returns>
    public int? ParsePlayerCount(string mapFilePath, string? displayName = null)
    {
        // 1. Check display name first if it has an explicit bracketed indicator like "(2) MapName" or "MapName [4]"
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var bracketMatch = BracketedPlayerCountRegex().Match(displayName);
            if (bracketMatch.Success && int.TryParse(bracketMatch.Groups[PlayersGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count is >= 1 and <= 8)
            {
                return count;
            }
        }

        // 2. Check file contents (numPlayers in Map section or Player_N_Start waypoints)
        var countFromFile = TryParsePlayerCountFromFile(mapFilePath);
        if (countFromFile.HasValue)
        {
            return countFromFile.Value;
        }

        // 3. Fallback to display name string matching
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var countFromName = ExtractPlayerCountFromString(displayName);
            if (countFromName.HasValue)
            {
                return countFromName.Value;
            }
        }

        // 4. Fallback to filename
        var fileName = Path.GetFileNameWithoutExtension(mapFilePath);
        var countFromFileName = ExtractPlayerCountFromString(fileName);
        if (countFromFileName.HasValue)
        {
            return countFromFileName.Value;
        }

        // 5. Fallback to directory name
        var dirName = Path.GetFileName(Path.GetDirectoryName(mapFilePath));
        if (!string.IsNullOrWhiteSpace(dirName))
        {
            var countFromDir = ExtractPlayerCountFromString(dirName);
            if (countFromDir.HasValue)
            {
                return countFromDir.Value;
            }
        }

        return null;
    }

    private static string CleanMapName(string name)
    {
        if (name.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        name = name.Replace('_', ' ');
        name = name.Replace('-', ' ');

        while (name.Contains("  ", StringComparison.Ordinal))
        {
            name = name.Replace("  ", " ", StringComparison.Ordinal);
        }

        return name.Trim();
    }

    private static int? TryParseNumPlayers(string line)
    {
        var match = NumPlayersRegex().Match(line);
        return match.Success && int.TryParse(match.Groups[PlayersGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) && num is >= 1 and <= 8
            ? num
            : null;
    }

    private static int? TryParseWaypointSlot(string line)
    {
        var match = WaypointSlotRegex().Match(line);
        return match.Success && int.TryParse(match.Groups[SlotGroupName].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) && slot is >= 1 and <= 8
            ? slot
            : null;
    }

    [GeneratedRegex(@"(?:\((?<players>[1-8])\)|\[(?<players>[1-8])\])", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedPlayerCountRegex();

    [GeneratedRegex(@"\b(?<players>[1-8])\s*(?:players?|player)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeywordPlayerCountRegex();

    [GeneratedRegex(@"\b(?<players>[1-8])\s*-\s*players?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HyphenatedPlayerCountRegex();

    [GeneratedRegex(@"(?:^|[\s_–\-])(?<players>[1-8])p(?=$|[\s_–\-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortPPlayerCountRegex();

    [GeneratedRegex(@"\bPlayer_(?<slot>[1-8])_Start\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WaypointSlotRegex();

    [GeneratedRegex(@"\bnumPlayers\s*=\s*(?<players>[1-8])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumPlayersRegex();

    /// <summary>
    /// Attempts to parse the map name from the .map file contents.
    /// </summary>
    /// <param name="mapFilePath">Path to the .map file.</param>
    /// <returns>The map name if found, otherwise null.</returns>
    private string? TryParseFromMapFile(string mapFilePath)
    {
        try
        {
            if (!File.Exists(mapFilePath))
            {
                return null;
            }

            using var reader = new StreamReader(mapFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            var inMapSection = false;

            while ((line = reader.ReadLine()) != null)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Equals("Map", StringComparison.OrdinalIgnoreCase))
                {
                    inMapSection = true;
                    continue;
                }

                if (inMapSection && trimmedLine.StartsWith("End", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (inMapSection && trimmedLine.StartsWith("displayName", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmedLine.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var displayName = parts[1].Trim().Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(displayName))
                        {
                            logger.LogDebug("Parsed map name from file: {Name}", displayName);
                            return displayName;
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse map name from file: {Path}", mapFilePath);
            return null;
        }
    }

    /// <summary>
    /// Attempts to parse the player count from the .map file contents.
    /// </summary>
    /// <param name="mapFilePath">Path to the .map file.</param>
    /// <returns>The player count if found, otherwise null.</returns>
    private int? TryParsePlayerCountFromFile(string mapFilePath)
    {
        try
        {
            if (!File.Exists(mapFilePath))
            {
                return null;
            }

            using var reader = new StreamReader(mapFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            var inMapSection = false;
            var maxWaypointSlot = 0;
            var linesRead = 0;

            // Cap line reads to prevent reading gigantic binary/compressed streams indefinitely
            while ((line = reader.ReadLine()) != null && linesRead++ < 20000)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Equals("Map", StringComparison.OrdinalIgnoreCase))
                {
                    inMapSection = true;
                    continue;
                }

                if (inMapSection)
                {
                    if (trimmedLine.StartsWith("End", StringComparison.OrdinalIgnoreCase))
                    {
                        inMapSection = false;
                    }
                    else if (TryParseNumPlayers(trimmedLine) is { } num)
                    {
                        logger.LogDebug("Parsed numPlayers from Map section: {Count}", num);
                        return num;
                    }
                }

                if (TryParseWaypointSlot(line) is { } slot && slot > maxWaypointSlot)
                {
                    maxWaypointSlot = slot;
                }
            }

            if (maxWaypointSlot > 0)
            {
                logger.LogDebug("Resolved player count from waypoint markers: {Count}", maxWaypointSlot);
                return maxWaypointSlot;
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse player count from file: {Path}", mapFilePath);
            return null;
        }
    }

    /// <summary>
    /// Falls back to using the directory name as the map name.
    /// </summary>
    /// <param name="mapFilePath">Path to the .map file.</param>
    /// <returns>The cleaned directory name, or null if not in a subdirectory.</returns>
    private string? FallbackToDirectoryName(string mapFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(mapFilePath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var directoryName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return null;
            }

            if (directoryName.Equals("Maps", StringComparison.OrdinalIgnoreCase) ||
                directoryName.Contains("Command and Conquer", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (directoryName.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            {
                directoryName = Path.GetFileNameWithoutExtension(directoryName);
            }

            logger.LogDebug("Using directory name as map name: {Name}", directoryName);
            return CleanMapName(directoryName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get directory name for: {Path}", mapFilePath);
            return null;
        }
    }
}
