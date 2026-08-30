using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for game content structure, archive payload normalization, and recognized game assets.
/// </summary>
public static class GameContentConstants
{
    /// <summary>
    /// Maximum recursive wrapper directory stripping depth.
    /// </summary>
    public const int MaxWrapperNormalizationDepth = 10;

    /// <summary>
    /// Supported archive file extensions.
    /// </summary>
    public static readonly IReadOnlyList<string> ArchiveExtensions =
    [
        ".zip",
        ".7z",
        ".rar",
        ".dat",
    ];

    /// <summary>
    /// Canonical directory names used at the game workspace root.
    /// </summary>
    public static readonly IReadOnlyList<string> RecognizedGameDirectories =
    [
        "Data",
        "Art",
        "Window",
        "Audio",
        "Maps",
        "INI",
        "Scripts",
        "Textures",
        "W3D",
        "English",
        "German",
        "French",
        "Italian",
        "Spanish",
        "Korean",
        "Polish",
        "Chinese",
    ];

    /// <summary>
    /// Canonical file extensions for game assets, binaries, and configurations.
    /// </summary>
    public static readonly IReadOnlyList<string> RecognizedGameFileExtensions =
    [
        ".big",
        ".exe",
        ".dll",
        ".str",
        ".csf",
        ".ini",
        ".map",
        ".bik",
        ".asi",
    ];

    /// <summary>
    /// Extensions for loose non-game documentation and metadata files.
    /// </summary>
    public static readonly IReadOnlyList<string> DocumentationExtensions =
    [
        ".txt",
        ".url",
        ".md",
        ".htm",
        ".html",
        ".pdf",
        ".lnk",
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
    ];

    /// <summary>
    /// System junk file or directory names to purge during payload normalization.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemJunkNames =
    [
        ".ds_store",
        "thumbs.db",
        "desktop.ini",
        "__macosx",
    ];

    /// <summary>
    /// Subfolder aliases that denote Zero Hour specific game content.
    /// </summary>
    public static readonly IReadOnlyList<string> ZeroHourSubfolderAliases =
    [
        "Zero Hour",
        "ZH",
        "Command and Conquer Generals Zero Hour",
        "Command & Conquer Generals - Zero Hour",
        "Command & Conquer: Generals - Zero Hour",
        "C&C Generals Zero Hour",
        "ZeroHour",
    ];

    /// <summary>
    /// Subfolder aliases that denote Generals specific game content.
    /// </summary>
    public static readonly IReadOnlyList<string> GeneralsSubfolderAliases =
    [
        "Generals",
        "CCG",
        "Command and Conquer Generals",
        "Command & Conquer Generals",
        "C&C Generals",
    ];

    /// <summary>
    /// Default variant resolution for control bar packages.
    /// </summary>
    public const string DefaultControlBarVariant = "1080p";

    /// <summary>
    /// Base filename for standard Control Bar Pro BIG archive.
    /// </summary>
    public const string ControlBarProBaseFileName = "340_ControlBarProZH.big";

    /// <summary>
    /// Base filename for Lemon Edition Control Bar Pro BIG archive.
    /// </summary>
    public const string ControlBarProLemonBaseFileName = "340_ControlBarProLemonEditionZH.big";

    /// <summary>
    /// Standard subfolder name for English BIG files.
    /// </summary>
    public const string BigEnDirectoryName = "BIG EN";

    /// <summary>
    /// Standard subfolder name for BIG files.
    /// </summary>
    public const string BigDirectoryName = "BIG";

    /// <summary>
    /// GenTool directory name.
    /// </summary>
    public const string GenToolDirectoryName = "GenTool";

    /// <summary>
    /// Window directory name.
    /// </summary>
    public const string WindowDirectoryName = "Window";

    /// <summary>
    /// Determines whether the specified directory name is a recognized canonical game directory.
    /// </summary>
    /// <param name="directoryName">The directory name to check.</param>
    /// <returns><c>true</c> if recognized; otherwise, <c>false</c>.</returns>
    public static bool IsRecognizedGameDirectory(string? directoryName)
    {
        return !string.IsNullOrEmpty(directoryName) &&
            RecognizedGameDirectories.Contains(directoryName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the specified file extension or file name represents a recognized game asset.
    /// </summary>
    /// <param name="fileNameOrExtension">The file name or extension to check.</param>
    /// <returns><c>true</c> if recognized; otherwise, <c>false</c>.</returns>
    public static bool IsRecognizedGameFile(string? fileNameOrExtension)
    {
        if (string.IsNullOrEmpty(fileNameOrExtension))
        {
            return false;
        }

        var ext = Path.GetExtension(fileNameOrExtension);
        if (string.IsNullOrEmpty(ext))
        {
            ext = fileNameOrExtension;
        }

        return RecognizedGameFileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
