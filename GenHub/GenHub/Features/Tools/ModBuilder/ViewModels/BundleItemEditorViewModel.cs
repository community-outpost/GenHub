using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Tools.ModBuilder.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing a bundle item.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound in XAML data templates")]
public partial class BundleItemEditorViewModel(ILocalizationService localizationService) : ObservableObject
{
    private const string NoPatternsKey = "Tools.ModBuilder.BundleItemEditor.MatchingFiles.NoPatterns";
    private const string DirectoryNotFoundKey = "Tools.ModBuilder.BundleItemEditor.MatchingFiles.DirectoryNotFound";
    private const string NoMatchesKey = "Tools.ModBuilder.BundleItemEditor.MatchingFiles.NoMatches";
    private const string MatchesOneKey = "Tools.ModBuilder.BundleItemEditor.MatchingFiles.MatchesOne";
    private const string MatchesManyKey = "Tools.ModBuilder.BundleItemEditor.MatchingFiles.MatchesMany";
    private const string CheckFailedKey = "Tools.ModBuilder.BundleItemEditor.MatchingFiles.CheckFailed";

    private bool _isUpdatingInternally;

    /// <summary>
    /// Gets or sets the name of the bundle item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the name prefix.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _namePrefix = string.Empty;

    /// <summary>
    /// Gets or sets the name suffix.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _nameSuffix = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this bundle should be packaged as a .big archive.
    /// </summary>
    [ObservableProperty]
    private bool _isBig = true;

    /// <summary>
    /// Gets or sets the suffix to add to the .big archive name.
    /// </summary>
    [ObservableProperty]
    private string _bigSuffix = string.Empty;

    /// <summary>
    /// Gets or sets the game language to set on installation.
    /// </summary>
    [ObservableProperty]
    private string _setGameLanguageOnInstall = string.Empty;

    /// <summary>
    /// Gets or sets the number of files in this bundle.
    /// </summary>
    [ObservableProperty]
    private int _fileCount;

    /// <summary>
    /// Gets or sets the file source pattern / glob for this bundle (e.g. GameFilesEdited/**/*.*).
    /// </summary>
    [ObservableProperty]
    private string _sourcePattern = string.Empty;

    /// <summary>
    /// Gets or sets the output conversion format (e.g. RAW, INI, TGA).
    /// Round-tripped so saves preserve byte-for-byte passthrough behavior.
    /// </summary>
    [ObservableProperty]
    private string? _outputFormat;

    /// <summary>
    /// Gets or sets a value indicating whether file conversion is skipped.
    /// </summary>
    [ObservableProperty]
    private bool _noConvert;

    /// <summary>
    /// Gets or sets the manifest file path for byte-for-byte reproducible BIG packing.
    /// </summary>
    [ObservableProperty]
    private string? _manifestFile;

    /// <summary>
    /// Gets or sets the bundle item description.
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// Gets or sets the target directory template applied to resolved files.
    /// </summary>
    [ObservableProperty]
    private string _targetDir = string.Empty;

    /// <summary>
    /// Gets or sets the base source directory for resolving relative patterns.
    /// </summary>
    [ObservableProperty]
    private string _baseDir = string.Empty;

    /// <summary>
    /// Gets the list of bundle pack links.
    /// </summary>
    public ObservableCollection<BundlePackLinkItemViewModel> PackLinks { get; } = [];

    /// <summary>
    /// Gets or sets the total number of matched files in the workspace.
    /// </summary>
    [ObservableProperty]
    private int _matchingFilesCount;

    /// <summary>
    /// Gets or sets the matching files summary label.
    /// </summary>
    [ObservableProperty]
    private string _matchingFilesSummary = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether manual/advanced raw pattern mode is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _isAdvancedPatternMode;

    /// <summary>
    /// Gets or sets custom pattern input text.
    /// </summary>
    [ObservableProperty]
    private string _customPatternInput = string.Empty;

    /// <summary>
    /// Gets or sets the list of individual source patterns and paths.
    /// Replaced as a whole on bulk updates so large pattern sets raise a single notification.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SourcePathItemViewModel> _sourcePatternsList = [];

    /// <summary>
    /// Gets the display name for the bundle item.
    /// </summary>
    public string DisplayName => $"{NamePrefix}{Name}{NameSuffix}";

    partial void OnSourcePatternChanged(string value)
    {
        if (_isUpdatingInternally)
        {
            return;
        }

        SyncListFromText(value);
    }

    /// <summary>
    /// Replaces the configured patterns with a new collection of patterns.
    /// </summary>
    /// <param name="patterns">The replacement patterns.</param>
    /// <param name="projectDir">Optional project directory to recalculate matches.</param>
    /// <param name="snapshot">Optional shared file snapshot to match against instead of walking the disk.</param>
    public void SetPatterns(IEnumerable<string> patterns, string? projectDir = null, ProjectFileSnapshot? snapshot = null)
    {
        SourcePatternsList = BuildPatternList(patterns);
        SyncTextFromList();
        RecalculateMatches(projectDir, snapshot);
    }

    /// <summary>
    /// Adds a pattern to this bundle item.
    /// </summary>
    /// <param name="pattern">The pattern or relative file path to add.</param>
    /// <param name="projectDir">Optional project root directory to recalculate matches.</param>
    /// <param name="snapshot">Optional shared file snapshot to match against instead of walking the disk.</param>
    public void AddPattern(string pattern, string? projectDir = null, ProjectFileSnapshot? snapshot = null)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        var normalized = pattern.Trim().Replace('\\', '/');

        // If the list only contains the generic default wildcard, replace it with specific pattern
        if (SourcePatternsList.Count == 1 && IsDefaultWildcardPattern(SourcePatternsList[0].Pattern))
        {
            SourcePatternsList.Clear();
        }

        if (!SourcePatternsList.Any(p => p.Pattern.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            SourcePatternsList.Add(new SourcePathItemViewModel(normalized));
            SyncTextFromList();
            RecalculateMatches(projectDir, snapshot);
        }
    }

    /// <summary>
    /// Adds multiple patterns to this bundle item with a single list refresh and match recalculation.
    /// </summary>
    /// <param name="patterns">The patterns or relative file paths to add.</param>
    /// <param name="projectDir">Optional project root directory to recalculate matches.</param>
    /// <param name="snapshot">Optional shared file snapshot to match against instead of walking the disk.</param>
    public void AddPatterns(IEnumerable<string> patterns, string? projectDir = null, ProjectFileSnapshot? snapshot = null)
    {
        var normalized = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Replace('\\', '/'))
            .ToList();
        if (normalized.Count == 0)
        {
            return;
        }

        // If the list only contains the generic default wildcard, replace it with specific patterns
        IEnumerable<string> existing = SourcePatternsList.Count == 1 && IsDefaultWildcardPattern(SourcePatternsList[0].Pattern)
            ? []
            : SourcePatternsList.Select(p => p.Pattern);

        SourcePatternsList = BuildPatternList(existing.Concat(normalized));
        SyncTextFromList();
        RecalculateMatches(projectDir, snapshot);
    }

    private static bool IsDefaultWildcardPattern(string pattern) =>
        pattern.Equals(ModBuilderConstants.GameFilesEditedAllFilesGlob, StringComparison.OrdinalIgnoreCase) ||
        pattern.Equals("**/*.*", StringComparison.OrdinalIgnoreCase);

    private static ObservableCollection<SourcePathItemViewModel> BuildPatternList(IEnumerable<string> patterns)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new ObservableCollection<SourcePathItemViewModel>();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var normalized = pattern.Trim().Replace('\\', '/');
            if (seen.Add(normalized))
            {
                items.Add(new SourcePathItemViewModel(normalized));
            }
        }

        return items;
    }

    /// <summary>
    /// Removes a pattern from this bundle item.
    /// </summary>
    /// <param name="item">The pattern item to remove.</param>
    /// <param name="projectDir">Optional project directory to recalculate matches.</param>
    /// <param name="snapshot">Optional shared file snapshot to match against instead of walking the disk.</param>
    public void RemovePattern(SourcePathItemViewModel item, string? projectDir = null, ProjectFileSnapshot? snapshot = null)
    {
        if (SourcePatternsList.Remove(item))
        {
            SyncTextFromList();
            RecalculateMatches(projectDir, snapshot);
        }
    }

    /// <summary>
    /// Clears all patterns.
    /// </summary>
    /// <param name="projectDir">Optional project directory.</param>
    /// <param name="snapshot">Optional shared file snapshot to match against instead of walking the disk.</param>
    public void ClearPatterns(string? projectDir = null, ProjectFileSnapshot? snapshot = null)
    {
        SourcePatternsList = [];
        SyncTextFromList();
        RecalculateMatches(projectDir, snapshot);
    }

    /// <summary>
    /// Recalculates the number of files matching all current patterns against the project directory.
    /// </summary>
    /// <param name="projectDir">The project directory.</param>
    /// <param name="snapshot">Optional shared file snapshot to match against instead of walking the disk.</param>
    public void RecalculateMatches(string? projectDir, ProjectFileSnapshot? snapshot = null)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir) || SourcePatternsList.Count == 0)
        {
            MatchingFilesCount = 0;
            MatchingFilesSummary = SourcePatternsList.Count == 0
                ? localizationService.GetString(NoPatternsKey)
                : localizationService.GetString(DirectoryNotFoundKey);
            return;
        }

        try
        {
            var effectiveSnapshot = snapshot is { } provided && provided.IsSameRoot(projectDir)
                ? provided
                : ProjectFileSnapshot.Create(projectDir);
            var count = effectiveSnapshot.CountMatches(SourcePatternsList.Select(item => item.Pattern));

            MatchingFilesCount = count;
            if (count == 0)
            {
                MatchingFilesSummary = localizationService.GetString(NoMatchesKey);
            }
            else
            {
                var matchesKey = count == 1 ? MatchesOneKey : MatchesManyKey;
                MatchingFilesSummary = localizationService.GetString(matchesKey, count);
            }
        }
        catch (Exception ex)
        {
            MatchingFilesSummary = localizationService.GetString(CheckFailedKey, ex.Message);
        }
    }

    private void SyncListFromText(string text)
    {
        _isUpdatingInternally = true;
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                SourcePatternsList = [];
                return;
            }

            var entries = text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var items = new ObservableCollection<SourcePathItemViewModel>();
            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                items.Add(new SourcePathItemViewModel(entry));
            }

            SourcePatternsList = items;
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void SyncTextFromList()
    {
        _isUpdatingInternally = true;
        try
        {
            SourcePattern = string.Join("; ", SourcePatternsList.Select(p => p.Pattern.Trim()));
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }
}
