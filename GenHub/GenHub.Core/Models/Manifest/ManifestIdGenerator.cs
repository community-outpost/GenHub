using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Utility for generating deterministic, human-readable manifest IDs.
/// </summary>
public static class ManifestIdGenerator
{
    /// <summary>
    /// Generates a manifest ID for publisher-provided content.
    /// Format: schemaVersion.manifestVersion.publisher.content.
    /// </summary>
    /// <param name="publisherId">Publisher identifier used as the first segment.</param>
    /// <param name="contentName">Human readable content name used as the second segment.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.manifestVersion.publisher.content'.</returns>
    public static string GeneratePublisherContentId(string publisherId, string contentName, int userVersion = 0)
    {
        if (string.IsNullOrWhiteSpace(publisherId))
            throw new ArgumentException("Publisher ID cannot be empty", nameof(publisherId));
        if (string.IsNullOrWhiteSpace(contentName))
            throw new ArgumentException("Content name cannot be empty", nameof(contentName));
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        var safePublisher = Normalize(publisherId);
        var safeName = Normalize(contentName);
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{userVersion}";

        // Handle empty segments by using a placeholder to maintain structure
        if (string.IsNullOrEmpty(safeName))
        {
            safeName = "unknown";
        }

        return $"{fullVersion}.{safePublisher}.{safeName}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation.
    /// Format: manifestVersion.userVersion.installationType.gameType.
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A normalized manifest identifier in the form 'manifestVersion.userVersion.installation.game'.</returns>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, int userVersion = 0)
    {
        if (installation == null)
            throw new ArgumentNullException(nameof(installation));
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        var installType = GetInstallationTypeString(installation.InstallationType);
        var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{userVersion}";

        return $"{fullVersion}.{installType}.{gameTypeString}";
    }

    /// <summary>
    /// Normalizes a string to lowercase alphanumeric with dots as separators.
    /// </summary>
    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var lower = input.ToLowerInvariant().Trim();

        // Replace non-alphanumeric characters (except dots) with dots
        var normalized = Regex.Replace(lower, "[^a-zA-Z0-9.]", ".");

        // Remove leading/trailing dots
        normalized = normalized.Trim('.');

        // Replace multiple consecutive dots with single dots
        normalized = Regex.Replace(normalized, "\\.+", ".");

        return normalized;
    }

    /// <summary>
    /// Gets a string representation for GameInstallationType.
    /// </summary>
    /// <param name="installationType">The installation type enum value.</param>
    /// <returns>A stable lowercase string representation.</returns>
    private static string GetInstallationTypeString(GameInstallationType installationType)
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
}
