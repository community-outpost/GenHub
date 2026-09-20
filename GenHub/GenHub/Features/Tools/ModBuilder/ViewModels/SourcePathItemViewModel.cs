using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// Represents an individual source path, folder glob, or file linked to a bundle item.
/// </summary>
public partial class SourcePathItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _pattern = string.Empty;

    [ObservableProperty]
    private string _typeLabel = "Pattern";

    [ObservableProperty]
    private string _iconKey = "IconOpenFolder";

    [ObservableProperty]
    private int _matchedFilesCount = -1;

    [ObservableProperty]
    private bool _isDirectoryGlob;

    [ObservableProperty]
    private string _displayFileName = string.Empty;

    [ObservableProperty]
    private string _displayDirectory = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourcePathItemViewModel"/> class.
    /// </summary>
    public SourcePathItemViewModel()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourcePathItemViewModel"/> class with a pattern.
    /// </summary>
    /// <param name="pattern">The pattern or path.</param>
    /// <param name="matchedCount">The number of matching files, or -1 if uncalculated.</param>
    public SourcePathItemViewModel(string pattern, int matchedCount = -1)
    {
        Pattern = pattern;
        MatchedFilesCount = matchedCount;
        (TypeLabel, IconKey, IsDirectoryGlob) = DetermineTypeAndIcon(pattern);
        (DisplayFileName, DisplayDirectory) = GetDisplayNames(pattern);
    }

    partial void OnPatternChanged(string value)
    {
        (TypeLabel, IconKey, IsDirectoryGlob) = DetermineTypeAndIcon(value);
        (DisplayFileName, DisplayDirectory) = GetDisplayNames(value);
    }

    private static (string DisplayFileName, string DisplayDirectory) GetDisplayNames(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return (string.Empty, string.Empty);
        }

        var normalized = pattern.Replace("\\", "/").Trim();
        if (IsFolderAllFilesGlob(normalized))
        {
            var cleanPath = normalized.Replace("/**/*.*", string.Empty)
                                      .Replace("/**", string.Empty)
                                      .Replace("/*.*", string.Empty);
            return (Path.GetFileName(cleanPath) + "/*", Path.GetDirectoryName(cleanPath)?.Replace("\\", "/") ?? string.Empty);
        }

        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            return (normalized[(lastSlash + 1)..], normalized[..lastSlash]);
        }

        return (normalized, string.Empty);
    }

    private static bool IsFolderAllFilesGlob(string path) =>
        path.EndsWith("/**/*.*", StringComparison.Ordinal) ||
        path.EndsWith("/**", StringComparison.Ordinal) ||
        path.EndsWith("/*.*", StringComparison.Ordinal);

    private static bool IsExtensionGlob(string path) =>
        path.Contains("/**/*.", StringComparison.Ordinal) ||
        path.Contains("/*.", StringComparison.Ordinal);

    private static (string TypeLabel, string IconKey) GetFileProperties(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var typeLabel = ext switch
        {
            ".tga" or ".dds" or ".png" => "Texture / Image",
            ".ini" => "INI Config",
            ".wnd" => "Window UI",
            ".csf" or ".str" => "String Table",
            ".wav" or ".mp3" => "Audio File",
            _ => "File",
        };
        var iconKey = ext switch
        {
            ".tga" or ".dds" or ".png" => "IconImageFile",
            _ => "IconTextFile",
        };
        return (typeLabel, iconKey);
    }

    private static (string TypeLabel, string IconKey, bool IsDirectoryGlob) DetermineTypeAndIcon(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return ("Empty", "IconTextFile", false);
        }

        var normalized = pattern.Replace('\\', '/').Trim();
        if (IsFolderAllFilesGlob(normalized))
        {
            return ("Folder (All Files)", "IconOpenFolder", true);
        }

        if (IsExtensionGlob(normalized))
        {
            var ext = Path.GetExtension(normalized);
            var label = string.IsNullOrEmpty(ext) ? "Glob Pattern" : $"{ext.TrimStart('.').ToUpperInvariant()} Glob";
            return (label, "IconSearch", true);
        }

        if (normalized.Contains('*', StringComparison.Ordinal) || normalized.Contains('?', StringComparison.Ordinal))
        {
            return ("Wildcard Glob", "IconSearch", false);
        }

        var (typeLabel, iconKey) = GetFileProperties(normalized);
        return (typeLabel, iconKey, false);
    }
}
