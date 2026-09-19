using CommunityToolkit.Mvvm.ComponentModel;
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
public partial class BundleItemEditorViewModel : ObservableObject
{
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
    /// Gets the list of individual source patterns and paths.
    /// </summary>
    public ObservableCollection<SourcePathItemViewModel> SourcePatternsList { get; } = [];

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
        SourcePatternsList.Clear();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var normalized = pattern.Trim().Replace("\\", "/");
            if (!SourcePatternsList.Any(p => p.Pattern.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                SourcePatternsList.Add(new SourcePathItemViewModel(normalized));
            }
        }

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
        if (SourcePatternsList.Count == 1 &&
            (SourcePatternsList[0].Pattern.Equals("GameFilesEdited/**/*.*", StringComparison.OrdinalIgnoreCase) ||
             SourcePatternsList[0].Pattern.Equals("**/*.*", StringComparison.OrdinalIgnoreCase)))
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
        SourcePatternsList.Clear();
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
                ? "No patterns defined"
                : "Project files directory not found";
            return;
        }

        try
        {
            var effectiveSnapshot = snapshot is { } provided && provided.IsSameRoot(projectDir)
                ? provided
                : ProjectFileSnapshot.Create(projectDir);
            var count = effectiveSnapshot.CountMatches(SourcePatternsList.Select(item => item.Pattern));

            MatchingFilesCount = count;
            var matchSuffix = count == 1 ? "file matches" : "files match";
            MatchingFilesSummary = count == 0
                ? "Warning: 0 files currently match these patterns"
                : $"✓ {count} {matchSuffix} in project";
        }
        catch (Exception ex)
        {
            MatchingFilesSummary = $"Match check: {ex.Message}";
        }
    }

    private void SyncListFromText(string text)
    {
        _isUpdatingInternally = true;
        try
        {
            SourcePatternsList.Clear();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var entries = text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                SourcePatternsList.Add(new SourcePathItemViewModel(entry));
            }
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
