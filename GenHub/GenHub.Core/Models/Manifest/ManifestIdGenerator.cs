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
    /// Format: publisherId.contentName.manifestSchemaVersion.
    /// </summary>
    /// <param name="publisherId">Publisher identifier used as the first segment.</param>
    /// <param name="contentName">Human readable content name used as the second segment.</param>
    /// <param name="manifestSchemaVersion">Manifest schema version in phased format (e.g., "3.25" = Phase 3, version 25). Defaults to current supported version.</param>
    /// <returns>A normalized manifest identifier in the form 'publisher.content.schemaVersion'.</returns>
    public static string GeneratePublisherContentId(string publisherId, string contentName, string manifestSchemaVersion = ManifestConstants.DefaultManifestSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(publisherId))
            throw new ArgumentException("Publisher ID cannot be empty", nameof(publisherId));
        if (string.IsNullOrWhiteSpace(contentName))
            throw new ArgumentException("Content name cannot be empty", nameof(contentName));
        if (string.IsNullOrWhiteSpace(manifestSchemaVersion))
            throw new ArgumentException("Manifest schema version cannot be empty", nameof(manifestSchemaVersion));

        var safePublisher = Normalize(publisherId);
        var safeName = Normalize(contentName);
        var safeVersion = NormalizeVersion(manifestSchemaVersion);

        // Handle empty segments by using a placeholder to maintain 3-segment structure
        if (string.IsNullOrEmpty(safeName))
        {
            safeName = "unknown";
        }

        return $"{safePublisher}.{safeName}.{safeVersion}";
    }

    /// <summary>
    /// Generates a manifest ID for a base game installation.
    /// Format: installationType.gameType.manifestSchemaVersion.
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="manifestSchemaVersion">Manifest schema version in phased format (e.g., "3.25" = Phase 3, version 25). Defaults to current supported version.</param>
    /// <returns>A normalized manifest identifier in the form 'installation.game.schemaVersion'.</returns>
    public static string GenerateBaseGameId(GameInstallation installation, GameType gameType, string manifestSchemaVersion = ManifestConstants.DefaultManifestSchemaVersion)
    {
        if (installation == null)
            throw new ArgumentNullException(nameof(installation));
        if (string.IsNullOrWhiteSpace(manifestSchemaVersion))
            throw new ArgumentException("Manifest schema version cannot be empty", nameof(manifestSchemaVersion));

        var installType = GetInstallationTypeString(installation.InstallationType);
        var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";
        var safeVersion = NormalizeVersion(manifestSchemaVersion);

        return $"{installType}.{gameTypeString}.{safeVersion}";
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
    /// Normalizes a version string, preserving dots and converting dashes and plus signs to dots.
    /// </summary>
    private static string NormalizeVersion(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var trimmed = input.Trim();

        // Convert dashes and plus signs to dots
        var withDots = trimmed.Replace('-', '.').Replace('+', '.');

        // Remove any other non-alphanumeric characters except dots
        var normalized = Regex.Replace(withDots, "[^a-zA-Z0-9.]", string.Empty);

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
            GameInstallationType.Origin => "origin",
            GameInstallationType.TheFirstDecade => "thefirstdecade",
            GameInstallationType.RGMechanics => "rgmechanics",
            GameInstallationType.CDISO => "cdiso",
            GameInstallationType.Wine => "wine",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(installationType), installationType, "Unknown installation type")
        };
    }
}
