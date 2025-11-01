using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
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
    /// Format: schemaVersion.manifestVersion.publisher.contentType.contentName[-suffix].
    /// </summary>
    /// <param name="publisherId">Publisher identifier used as the first segment (e.g., 'cnclabs', 'moddb-westwood').</param>
    /// <param name="contentType">The type of content being identified.</param>
    /// <param name="contentName">Human readable content name used as the second segment.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="suffix">Optional suffix for content type (e.g., '-mod', '-mappack'). Defaults to empty.</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.manifestVersion.publisher.contentType.content[suffix]'.</returns>
    public static string GeneratePublisherContentId(string publisherId, ContentType contentType, string contentName, int userVersion = 0, string suffix = "")
    {
        if (publisherId == null)
            throw new ArgumentNullException(nameof(publisherId));
        if (contentName == null)
            throw new ArgumentNullException(nameof(contentName));
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        var safePublisher = Normalize(publisherId);
        var contentTypeString = GetContentTypeString(contentType);
        var safeName = Normalize(contentName);
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{userVersion}";

        var contentPart = string.IsNullOrEmpty(suffix) ? safeName : $"{safeName}{suffix}";
        return $"{fullVersion}.{safePublisher}.{contentTypeString}.{contentPart}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation.
    /// Format: manifestVersion.userVersion.installationType.gameType[-suffix].
    /// Note: If userVersion contains dots (e.g., "1.08"), they are removed for schema compliance (becomes "108").
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <param name="suffix">Optional suffix for content type (e.g., '-installation', '-client'). Defaults to '-installation'.</param>
    /// <returns>A normalized manifest identifier in the form 'manifestVersion.userVersion.installationType.gameType[suffix]'.</returns>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, string? userVersion, string suffix = "")
    {
        if (installation == null)
            throw new ArgumentNullException(nameof(installation));

        // Normalize user version - remove dots and convert to string
        string normalizedUserVersion = NormalizeVersionString(userVersion);

        var installType = installation.InstallationType.ToIdentifierString();
        var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{normalizedUserVersion}";

        return $"{fullVersion}.{installType}.{gameTypeString}{suffix}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation with integer version.
    /// Format: manifestVersion.userVersion.installationType.gameType[-suffix].
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version (e.g., 0, 1, 2). Defaults to 0.</param>
    /// <param name="suffix">Optional suffix for content type (e.g., '-installation', '-client'). Defaults to '-installation'.</param>
    /// <returns>A normalized manifest identifier in the form 'manifestVersion.userVersion.installationType.gameType[suffix]'.</returns>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, int userVersion = 0, string suffix = "")
    {
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        return GenerateGameInstallationId(installation, gameType, userVersion.ToString(), suffix);
    }

    /// <summary>
    /// Normalizes a version value to a string without dots.
    /// Examples: "1.08" → "108", "1.04" → "104", 5 → "5", "2.0" → "20", null → "0".
    /// </summary>
    /// <param name="version">Version as string or null (defaults to "0").</param>
    /// <returns>Normalized version string without dots.</returns>
    private static string NormalizeVersionString(string? version)
    {
        if (string.IsNullOrEmpty(version))
            return "0";

        string versionStr = version;

        // Handle dotted versions like "1.08" or "2.0"
        if (versionStr.Contains('.'))
        {
            var parts = versionStr.Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
                throw new ArgumentException($"Version must be in format 'major.minor': {version}", nameof(version));

            // Preserve semantic meaning: "1.08" → "108", "1.8" → "108", "2.0" → "200"
            return $"{major}{minor.ToString().PadLeft(2, '0')}";
        }

        // Handle integer versions directly
        if (!int.TryParse(versionStr, out int parsedVersion) || parsedVersion < 0)
            throw new ArgumentException($"Version must be numeric and non-negative: {version}", nameof(version));

        return versionStr;
    }

    /// <summary>
    /// Normalizes a string to lowercase alphanumeric with dots as separators.
    /// </summary>
    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be null or whitespace", nameof(input));

        var lower = input.ToLowerInvariant().Trim();

        // Replace non-alphanumeric characters (except dots) with dots
        var normalized = Regex.Replace(lower, "[^a-zA-Z0-9.]", ".");

        // Remove leading/trailing dots
        normalized = normalized.Trim('.');

        // Replace multiple consecutive dots with single dots
        normalized = Regex.Replace(normalized, "\\.+", ".");

        return string.IsNullOrEmpty(normalized) ? throw new ArgumentException("Input results in empty string after normalization", nameof(input)) : normalized;
    }

    /// <summary>
    /// Gets a string representation for ContentType.
    /// </summary>
    /// <param name="contentType">The content type enum value.</param>
    /// <returns>A stable lowercase string representation.</returns>
    private static string GetContentTypeString(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.GameInstallation => "gameinstallation",
            ContentType.GameClient => "gameclient",
            ContentType.Mod => "mod",
            ContentType.Patch => "patch",
            ContentType.Addon => "addon",
            ContentType.MapPack => "mappack",
            ContentType.LanguagePack => "languagepack",
            ContentType.ContentBundle => "contentbundle",
            ContentType.PublisherReferral => "publisherreferral",
            ContentType.ContentReferral => "contentreferral",
            ContentType.Mission => "mission",
            ContentType.Map => "map",
            ContentType.UnknownContentType => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Unknown content type")
        };
    }
}
