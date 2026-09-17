using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

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
        DetermineTypeAndIcon();
    }

    partial void OnPatternChanged(string value)
    {
        DetermineTypeAndIcon();
    }

    private void DetermineTypeAndIcon()
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            TypeLabel = "Empty";
            IconKey = "IconTextFile";
            IsDirectoryGlob = false;
            return;
        }

        var normalized = Pattern.Replace('\\', '/').Trim();
        if (normalized.EndsWith("/**/*.*") || normalized.EndsWith("/**") || normalized.EndsWith("/*.*"))
        {
            TypeLabel = "Folder (All Files)";
            IconKey = "IconOpenFolder";
            IsDirectoryGlob = true;
        }
        else if (normalized.Contains("/**/*.") || normalized.Contains("/*."))
        {
            var ext = Path.GetExtension(normalized);
            TypeLabel = string.IsNullOrEmpty(ext) ? "Glob Pattern" : $"{ext.TrimStart('.').ToUpperInvariant()} Glob";
            IconKey = "IconSearch";
            IsDirectoryGlob = true;
        }
        else if (normalized.Contains('*') || normalized.Contains('?'))
        {
            TypeLabel = "Wildcard Glob";
            IconKey = "IconSearch";
            IsDirectoryGlob = false;
        }
        else
        {
            var ext = Path.GetExtension(normalized).ToLowerInvariant();
            TypeLabel = ext switch
            {
                ".tga" or ".dds" or ".png" => "Texture / Image",
                ".ini" => "INI Config",
                ".wnd" => "Window UI",
                ".csf" or ".str" => "String Table",
                ".wav" or ".mp3" => "Audio File",
                _ => "File",
            };
            IconKey = ext switch
            {
                ".tga" or ".dds" or ".png" => "IconImageFile",
                _ => "IconTextFile",
            };
            IsDirectoryGlob = false;
        }
    }
}
