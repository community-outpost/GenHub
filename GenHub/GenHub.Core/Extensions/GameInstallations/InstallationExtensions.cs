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
        logger?.LogTrace(
            "Converting {InstallationType} installation to domain model",
            installation.InstallationType);

        var installationPath = installation.HasGenerals ? installation.GeneralsPath : installation.ZeroHourPath;
        if (string.IsNullOrEmpty(installationPath))
        {
            installationPath = installation.InstallationPath;
        }

        var gameInstallation = new GameInstallation(installationPath, installation.InstallationType, logger as ILogger<GameInstallation>);
        gameInstallation.Id = installation.Id;
        gameInstallation.SetPaths(installation.GeneralsPath, installation.ZeroHourPath);
        gameInstallation.PopulateGameClients(installation.AvailableGameClients);

        logger?.LogTrace(
            "Successfully converted installation to domain model: {InstallationPath}, HasGenerals={HasGenerals}, HasZeroHour={HasZeroHour}",
            installationPath,
            gameInstallation.HasGenerals,
            gameInstallation.HasZeroHour);
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
            GameInstallationType.Wine => "Wine/Proton",
            GameInstallationType.CDISO => "CD/ISO",
            GameInstallationType.Retail => "Retail",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Gets a normalized string representation for the installation type, suitable for manifest IDs and identifiers.
    /// Returns lowercase identifiers for consistency with the manifest ID system.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>A stable normalized lowercase string representation.</returns>
    public static string ToIdentifierString(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "steam",
            GameInstallationType.EaApp => "eaapp",
            GameInstallationType.TheFirstDecade => "thefirstdecade",
            GameInstallationType.CDISO => "cdiso",
            GameInstallationType.Wine => "wine",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(installationType), installationType, "Unknown installation type")
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
            GameInstallationType.Wine => false,
            GameInstallationType.CDISO => false,
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

    /// <summary>
    /// Maps GameInstallationType enum to installation source identifier string.
    /// This is the canonical mapping used across the codebase for installation-source semantics.
    /// </summary>
    /// <param name="installationType">The game installation type to convert.</param>
    /// <returns>The corresponding installation source identifier string.</returns>
    public static string ToInstallationSourceString(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "steam",
            GameInstallationType.EaApp => "eaapp",
            GameInstallationType.TheFirstDecade => "thefirstdecade",
            GameInstallationType.Wine => "wine",
            GameInstallationType.CDISO => "cdiso",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Unknown => "unknown",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Maps GameInstallationType enum to publisher type identifier string.
    /// This mapping is intentionally separate because publisher semantics may differ from installation-source semantics.
    /// </summary>
    /// <param name="installationType">The game installation type to convert.</param>
    /// <returns>The corresponding publisher type identifier string.</returns>
    public static string ToPublisherTypeString(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "steam",
            GameInstallationType.EaApp => "eaapp",

            // TheFirstDecade is mapped to Retail for publisher purposes (legacy/branding)
            GameInstallationType.TheFirstDecade => "retail",

            // Wine/Proton installations are treated as retail-published content
            GameInstallationType.Wine => "retail",
            GameInstallationType.CDISO => "retail",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Unknown => "unknown",
            _ => "unknown",
        };
    }
}
