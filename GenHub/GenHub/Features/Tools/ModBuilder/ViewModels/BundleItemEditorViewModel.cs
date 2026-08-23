using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing a bundle item.
/// </summary>
public partial class BundleItemEditorViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the bundle item.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the name prefix.
    /// </summary>
    [ObservableProperty]
    private string _namePrefix = string.Empty;

    /// <summary>
    /// Gets or sets the name suffix.
    /// </summary>
    [ObservableProperty]
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
    /// Gets the display name for the bundle item.
    /// </summary>
    public string DisplayName => $"{NamePrefix}{Name}{NameSuffix}";

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNamePrefixChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNameSuffixChanged(string value) => OnPropertyChanged(nameof(DisplayName));
}
