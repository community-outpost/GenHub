namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Option model representing an installed game target in the dropdown.
/// </summary>
public sealed class SharedInstallationOption
{
    /// <summary>
    /// Gets the unique identifier of the installation.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name for the UI dropdown.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the directory path of the installation.
    /// </summary>
    public required string InstallationPath { get; init; }
}
