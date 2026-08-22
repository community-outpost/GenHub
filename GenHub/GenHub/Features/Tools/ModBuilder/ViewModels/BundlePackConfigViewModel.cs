using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing bundle pack configuration.
/// </summary>
public partial class BundlePackConfigViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the bundle pack.
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
    /// Gets or sets a value indicating whether this pack should be built.
    /// </summary>
    [ObservableProperty]
    private bool _allowBuild = false;

    /// <summary>
    /// Gets or sets a value indicating whether this pack can be installed.
    /// </summary>
    [ObservableProperty]
    private bool _allowInstall = false;

    /// <summary>
    /// Gets or sets the game language to set on installation.
    /// </summary>
    [ObservableProperty]
    private string _setGameLanguageOnInstall = string.Empty;

    /// <summary>
    /// Gets the list of bundle item names included in this pack.
    /// </summary>
    public ObservableCollection<string> ItemNames { get; } = [];

    /// <summary>
    /// Gets the display name for the bundle pack.
    /// </summary>
    public string DisplayName => $"{NamePrefix}{Name}{NameSuffix}";

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNamePrefixChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNameSuffixChanged(string value) => OnPropertyChanged(nameof(DisplayName));
}
