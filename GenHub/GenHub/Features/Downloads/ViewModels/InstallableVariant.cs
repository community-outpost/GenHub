using System.Windows.Input;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents a specific installable variant for multi-asset content.
/// </summary>
public class InstallableVariant
{
    /// <summary>
    /// Gets or sets the display name of the variant.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the specific manifest ID for this variant.
    /// </summary>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command to add this variant to a profile.
    /// Expects a GameProfile as parameter.
    /// </summary>
    public ICommand? AddToProfileCommand { get; set; }

    /// <summary>
    /// Gets or sets the icon URL for this variant (usually matches the main item).
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;
}
