using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Extensions.GameInstallations;

/// <summary>
/// Extension methods for game installation types.
/// </summary>
public static class InstallationExtensions
{
    /// <summary>
    /// Converts a platform-specific installation to the domain model.
    /// </summary>
    /// <param name="installation">The platform installation.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <returns>Domain model game installation.</returns>
    public static GameInstallation ToDomain(this IGameInstallation installation, ILogger? logger = null)
    {
        logger?.LogDebug("Converting {InstallationType} installation to domain model", installation.InstallationType);

        var installationPath = installation.HasGenerals ? installation.GeneralsPath : installation.ZeroHourPath;
        if (string.IsNullOrEmpty(installationPath))
        {
            installationPath = installation.InstallationPath;
        }

        var gameInstallation = new GameInstallation(installationPath, installation.InstallationType, logger as ILogger<GameInstallation>);

        logger?.LogDebug("Successfully converted installation to domain model: {InstallationPath}", installationPath);
        return gameInstallation;
    }

    /// <summary>
    /// Gets a human-readable display name for the installation type.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>Display name.</returns>
    public static string GetDisplayName(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "Steam",
            GameInstallationType.EaApp => "EA App",
            GameInstallationType.Origin => "Origin",
            GameInstallationType.Wine => "Wine/Proton",
            GameInstallationType.Retail => "Retail",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Determines if the installation type supports automatic updates.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>True if automatic updates are supported.</returns>
    public static bool SupportsAutomaticUpdates(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => true,
            GameInstallationType.EaApp => true,
            GameInstallationType.Origin => true,
            GameInstallationType.Wine => false,
            GameInstallationType.Retail => false,
            _ => false
        };
    }

    /// <summary>
    /// Determines if the installation type requires Wine/Proton compatibility layer.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>True if Wine/Proton is required.</returns>
    public static bool RequiresWineCompatibility(this GameInstallationType installationType)
    {
        return installationType == GameInstallationType.Wine;
    }

    /// <summary>
    /// Validates an installation and logs the result.
    /// </summary>
    /// <param name="installation">The installation to validate.</param>
    /// <param name="logger">Logger instance.</param>
    /// <returns>True if the installation is valid.</returns>
    public static bool ValidateInstallation(
        this IGameInstallation installation,
        ILogger? logger = null)
    {
        logger?.LogDebug(
            "Validating installation: {InstallationType} at {InstallationPath}",
            installation.InstallationType,
            installation.InstallationPath);

        var hasValidGenerals = !installation.HasGenerals ||
            (!string.IsNullOrEmpty(installation.GeneralsPath) && System.IO.Directory.Exists(installation.GeneralsPath));

        var hasValidZeroHour = !installation.HasZeroHour ||
            (!string.IsNullOrEmpty(installation.ZeroHourPath) && System.IO.Directory.Exists(installation.ZeroHourPath));

        var isValid = hasValidGenerals && hasValidZeroHour;

        logger?.LogDebug(
            "Installation validation result: {IsValid} (Generals: {HasValidGenerals}, ZeroHour: {HasValidZeroHour})",
            isValid,
            hasValidGenerals,
            hasValidZeroHour);

        return isValid;
    }
}
