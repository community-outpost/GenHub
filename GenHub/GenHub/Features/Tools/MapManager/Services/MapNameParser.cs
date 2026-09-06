using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Service for parsing map display names from .map files and directories.
/// </summary>
public class MapNameParser(ILogger<MapNameParser> logger)
{
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

        return Path.GetFileNameWithoutExtension(mapFilePath);
    }

    private static string CleanMapName(string name)
    {
        name = name.Replace('_', ' ');
        name = name.Replace('-', ' ');

        while (name.Contains("  ", StringComparison.Ordinal))
        {
            name = name.Replace("  ", " ", StringComparison.Ordinal);
        }

        return name.Trim();
    }

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
