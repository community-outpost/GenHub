using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.UserData.Services;

/// <summary>
/// Provides shared path normalization and resolution utilities for user data files
/// (maps, replays, screenshots, and general user data).
/// </summary>
internal static class UserDataPathHelper
{
    /// <summary>
    /// Extracts distinct candidate map names from the given manifest files targeting the user maps directory.
    /// </summary>
    /// <param name="userDataFiles">The collection of manifest files to analyze.</param>
    /// <returns>A list of distinct candidate map names.</returns>
    public static List<string> ExtractCandidateMapNames(IEnumerable<ManifestFile> userDataFiles)
    {
        return ExtractCandidateMapNames(userDataFiles
            .Where(f => f.InstallTarget == ContentInstallTarget.UserMapsDirectory)
            .Select(f => f.RelativePath));
    }

    /// <summary>
    /// Extracts distinct candidate map names from a sequence of relative map file paths.
    /// </summary>
    /// <param name="mapRelativePaths">The relative map file paths to analyze.</param>
    /// <returns>A list of distinct candidate map names.</returns>
    public static List<string> ExtractCandidateMapNames(IEnumerable<string> mapRelativePaths)
    {
        return mapRelativePaths
            .Select(p => StripLeadingDirectory(p.Replace('\\', '/').Trim('/'), GameSettingsConstants.FolderNames.Maps))
            .Where(p => p.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p);
                return name.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(name)
                    : name;
            })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Normalizes a relative path within its target container folder (Maps, Replays, Screenshots)
    /// to provide consistent relative paths across flat, nested, and companion file structures.
    /// </summary>
    /// <param name="installTarget">The install target folder destination.</param>
    /// <param name="relativePath">The relative path of the file.</param>
    /// <param name="singleMapBaseName">Optional map name if the manifest contains exactly one map.</param>
    /// <param name="candidateMapNames">Optional candidate map names for disambiguating multi-map archives.</param>
    /// <returns>The normalized relative path.</returns>
    public static string NormalizeUserDataRelativePath(
        ContentInstallTarget installTarget,
        string relativePath,
        string? singleMapBaseName = null,
        IReadOnlyList<string>? candidateMapNames = null)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return installTarget switch
        {
            ContentInstallTarget.UserMapsDirectory =>
                ResolveMapRelativePath(normalized, singleMapBaseName, candidateMapNames),
            ContentInstallTarget.UserReplaysDirectory =>
                StripLeadingDirectory(normalized, GameSettingsConstants.FolderNames.Replays),
            ContentInstallTarget.UserScreenshotsDirectory =>
                StripLeadingDirectory(normalized, GameSettingsConstants.FolderNames.Screenshots),
            _ => normalized,
        };
    }

    /// <summary>
    /// Resolves the absolute target path on disk for a given user data file, ensuring it remains
    /// strictly within the user data base directory.
    /// </summary>
    /// <param name="installTarget">The install target folder destination.</param>
    /// <param name="relativePath">The relative path of the file.</param>
    /// <param name="userDataBasePath">The absolute path to the user data base folder.</param>
    /// <param name="singleMapBaseName">Optional map name if the manifest contains exactly one map.</param>
    /// <param name="candidateMapNames">Optional candidate map names for disambiguating multi-map archives.</param>
    /// <returns>The resolved absolute target path.</returns>
    public static string ResolveUserDataTargetPath(
        ContentInstallTarget installTarget,
        string relativePath,
        string userDataBasePath,
        string? singleMapBaseName = null,
        IReadOnlyList<string>? candidateMapNames = null)
    {
        var normalizedRelative = NormalizeUserDataRelativePath(installTarget, relativePath, singleMapBaseName, candidateMapNames);
        var subDir = installTarget switch
        {
            ContentInstallTarget.UserMapsDirectory => GameSettingsConstants.FolderNames.Maps,
            ContentInstallTarget.UserReplaysDirectory => GameSettingsConstants.FolderNames.Replays,
            ContentInstallTarget.UserScreenshotsDirectory => GameSettingsConstants.FolderNames.Screenshots,
            _ => string.Empty,
        };

        var targetPath = string.IsNullOrEmpty(subDir)
            ? Path.Combine(userDataBasePath, normalizedRelative)
            : Path.Combine(userDataBasePath, subDir, normalizedRelative);

        var fullPath = Path.GetFullPath(targetPath);
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDataBasePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"Relative path escapes the user data directory: {relativePath}");
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves the relative path for a map file to ensure it conforms to C&amp;C Generals / Zero Hour
    /// map directory requirements (Maps/&lt;MapName&gt;/&lt;MapName&gt;.map, &lt;MapName&gt;.tga, etc.).
    /// </summary>
    /// <param name="relativePath">The relative path of the map file.</param>
    /// <param name="fallbackMapName">Optional fallback map name for flat companion files.</param>
    /// <param name="candidateMapNames">Optional candidate map names for disambiguating multi-map archives.</param>
    /// <returns>The resolved relative path within the Maps folder.</returns>
    public static string ResolveMapRelativePath(
        string relativePath,
        string? fallbackMapName = null,
        IReadOnlyList<string>? candidateMapNames = null)
    {
        var pathUnderMaps = StripLeadingDirectory(relativePath, GameSettingsConstants.FolderNames.Maps);
        var normalized = pathUnderMaps.Replace('\\', '/').Trim('/');

        var slashIdx = normalized.LastIndexOf('/');
        if (slashIdx < 0)
        {
            return ResolveFlatMapRelativePath(normalized, fallbackMapName, candidateMapNames);
        }

        return ResolveNestedMapRelativePath(normalized, slashIdx);
    }

    /// <summary>
    /// Checks whether an extension is a known map file or companion format (.map, .tga, .ini, .str, .wak).
    /// </summary>
    /// <param name="ext">The file extension including leading dot.</param>
    /// <returns><c>true</c> if supported; otherwise <c>false</c>.</returns>
    public static bool IsSupportedMapExtension(string ext) =>
        ext.Equals(".map", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".str", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".wak", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strips a leading directory name from a relative path if present.
    /// </summary>
    /// <param name="path">The relative path to process.</param>
    /// <param name="directoryName">The directory name prefix to strip.</param>
    /// <returns>The path stripped of the leading directory prefix, or the original path.</returns>
    public static string StripLeadingDirectory(string path, string directoryName)
    {
        var normalized = path.Replace('\\', '/');
        var prefix = directoryName + "/";

        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[prefix.Length..].TrimStart('/');
        }

        return normalized;
    }

    private static string ResolveFlatMapRelativePath(
        string normalized,
        string? fallbackMapName,
        IReadOnlyList<string>? candidateMapNames)
    {
        var ext = Path.GetExtension(normalized);
        if (!IsSupportedMapExtension(ext))
        {
            return normalized;
        }

        var baseName = Path.GetFileNameWithoutExtension(normalized);
        var (folderName, fileName) = ResolveFlatMapComponents(baseName, ext, normalized, fallbackMapName, candidateMapNames);
        return Path.Combine(folderName, fileName).Replace('\\', '/');
    }

    private static (string FolderName, string FileName) ResolveFlatMapComponents(
        string baseName,
        string ext,
        string originalFileName,
        string? fallbackMapName,
        IReadOnlyList<string>? candidateMapNames)
    {
        var isTga = ext.Equals(".tga", StringComparison.OrdinalIgnoreCase);
        var genericFileName = ResolveGenericMapFileName(baseName, ext, fallbackMapName);
        if (genericFileName != null && !string.IsNullOrEmpty(fallbackMapName))
        {
            return (fallbackMapName, genericFileName);
        }

        if (baseName.EndsWith("_art", StringComparison.OrdinalIgnoreCase))
        {
            var stripped = baseName[..^4];
            var resolvedFile = isTga ? stripped + ".tga" : originalFileName;
            return (stripped, resolvedFile);
        }

        var matchedMap = FindMatchingCandidateMap(baseName, candidateMapNames);
        if (!string.IsNullOrEmpty(matchedMap))
        {
            var isCaseOnlyMatch = isTga && string.Equals(baseName, matchedMap, StringComparison.OrdinalIgnoreCase);
            var resolvedFile = isCaseOnlyMatch ? matchedMap + ".tga" : originalFileName;
            return (matchedMap, resolvedFile);
        }

        return (baseName, originalFileName);
    }

    private static string? ResolveGenericMapFileName(string baseName, string ext, string? mapName)
    {
        if (ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) &&
            (baseName.Equals("map", StringComparison.OrdinalIgnoreCase) ||
             baseName.Equals("preview", StringComparison.OrdinalIgnoreCase)))
        {
            return string.IsNullOrEmpty(mapName) ? null : mapName + ".tga";
        }

        if (!baseName.Equals("map", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ext.ToLowerInvariant() switch
        {
            ".ini" => MapManagerConstants.MapIniFileName,
            ".str" => MapManagerConstants.MapStrFileName,
            _ => null,
        };
    }

    private static string? FindMatchingCandidateMap(string baseName, IReadOnlyList<string>? candidateMapNames)
    {
        if (candidateMapNames is null || candidateMapNames.Count == 0)
        {
            return null;
        }

        // 1. Exact match first across all candidates
        var exact = candidateMapNames.FirstOrDefault(m => baseName.Equals(m, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // 2. Prefix match: choose the longest candidate prefix to prevent shadowing (e.g. River_v2 over River)
        return candidateMapNames
            .Where(m => baseName.StartsWith(m + "_", StringComparison.OrdinalIgnoreCase) ||
                        baseName.StartsWith(m + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Length)
            .FirstOrDefault();
    }

    private static string ResolveNestedMapRelativePath(string normalized, int slashIdx)
    {
        var directoryPart = normalized[..slashIdx];
        var fileName = normalized[(slashIdx + 1)..];

        var folderName = Path.GetFileName(directoryPart);
        if (folderName.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        {
            folderName = Path.GetFileNameWithoutExtension(folderName);
        }

        var fileExt = Path.GetExtension(fileName);
        var fileBase = Path.GetFileNameWithoutExtension(fileName);

        if (fileExt.Equals(".tga", StringComparison.OrdinalIgnoreCase) &&
            (fileBase.Equals("map", StringComparison.OrdinalIgnoreCase) ||
             fileBase.Equals("preview", StringComparison.OrdinalIgnoreCase) ||
             fileBase.EndsWith("_art", StringComparison.OrdinalIgnoreCase)))
        {
            fileName = folderName + ".tga";
        }

        return Path.Combine(folderName, fileName).Replace('\\', '/');
    }
}
