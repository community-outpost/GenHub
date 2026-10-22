namespace GenHub.Core.Models.GameProfiles;

/// <summary>
/// Represents a profile information item for a game profile.
/// </summary>
public class ProfileInfoItem
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source type name.
    /// </summary>
    public string SourceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the install path.
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted size.
    /// </summary>
    public string FormattedSize { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build date.
    /// </summary>
    public string BuildDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source type.
    /// </summary>
    public string SourceType { get; set; } = string.Empty;
}
