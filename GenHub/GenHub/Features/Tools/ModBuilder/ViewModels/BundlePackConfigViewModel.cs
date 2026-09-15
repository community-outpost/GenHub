using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing bundle pack configuration.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound in XAML data templates")]
public partial class BundlePackConfigViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the bundle pack.
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
    /// Gets or sets a value indicating whether this pack is packaged as a .big file instead of a zip file.
    /// </summary>
    [ObservableProperty]
    private bool _big = false;

    /// <summary>
    /// Gets or sets the custom output file name for this bundle pack.
    /// </summary>
    [ObservableProperty]
    private string? _outputFile;

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

    /// <summary>
    /// Synchronizes the OutputFile extension when the Big property changes.
    /// </summary>
    /// <param name="value">The new value of Big.</param>
    partial void OnBigChanged(bool value)
    {
        if (!string.IsNullOrWhiteSpace(OutputFile))
        {
            if (value && OutputFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                OutputFile = Path.ChangeExtension(OutputFile, ".big").Replace('\\', '/');
            }
            else if (!value && OutputFile.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
            {
                OutputFile = Path.ChangeExtension(OutputFile, ".zip").Replace('\\', '/');
            }
        }
    }
}
