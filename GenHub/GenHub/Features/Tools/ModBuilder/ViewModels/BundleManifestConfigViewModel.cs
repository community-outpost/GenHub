using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing bundle manifest definitions.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound in XAML data templates")]
public partial class BundleManifestConfigViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the bundle manifest.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the manifest version string.
    /// </summary>
    [ObservableProperty]
    private string _version = ModBuilderConstants.DefaultManifestVersion;

    /// <summary>
    /// Gets or sets the publisher identifier. Empty selects the default local publisher.
    /// </summary>
    [ObservableProperty]
    private string _publisher = string.Empty;

    /// <summary>
    /// Gets or sets the bundle manifest description.
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>
    /// Gets or sets the content type for the created manifest.
    /// </summary>
    [ObservableProperty]
    private ContentType _contentType = ContentType.Mod;

    /// <summary>
    /// Gets or sets the target game for the created manifest.
    /// </summary>
    [ObservableProperty]
    private GameType _targetGame = GameType.ZeroHour;

    /// <summary>
    /// Gets the names of the bundle packs linked into this manifest.
    /// </summary>
    public ObservableCollection<string> PackNames { get; } = [];

    /// <summary>
    /// Gets the display name for the bundle manifest.
    /// </summary>
    public string DisplayName => Name;
}
