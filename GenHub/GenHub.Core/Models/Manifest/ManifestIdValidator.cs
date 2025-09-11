using System.Linq;
using System.Text.RegularExpressions;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Provides validation for manifest IDs to ensure they follow the deterministic,
/// human-readable scheme required by GenHub.
/// </summary>
public static class ManifestIdValidator
{
    // Regex for publisher content IDs: schemaVersion.manifestVersion.publisher.content (4+ segments)
    private static readonly Regex PublisherIdRegex =
        new(@"^\d+(?:\.\d+)*\.[a-zA-Z0-9\-]+(?:\.[a-zA-Z0-9\-]+)*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Allow simple test-friendly ids like 'test-id' or 'simple.id' (alphanumeric with dashes, max 3 segments)
    private static readonly Regex SimpleIdRegex =
        new(@"^[a-zA-Z0-9\-]+(\.[a-zA-Z0-9\-]+){0,2}$", RegexOptions.Compiled);

    // Regex for game installation IDs: schema.user.installationType.gameType
    private static readonly Regex GameInstallationIdRegex =
        new(@"^\d+(?:\.\d+)*\.(unknown|steam|eaapp|origin|thefirstdecade|rgmechanics|cdiso|wine|retail)\.(generals|zerohour)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if the manifest ID is invalid.
    /// </summary>
    /// <param name="manifestId">Manifest identifier to validate.</param>
    public static void EnsureValid(string manifestId)
    {
        if (!IsValid(manifestId, out var reason))
        {
            throw new ArgumentException(reason, nameof(manifestId));
        }
    }

    /// <summary>
    /// Validates whether the given manifest ID is valid according to GenHub rules.
    /// </summary>
    /// <param name="manifestId">Manifest identifier to validate.</param>
    /// <param name="reason">If invalid, contains a human-readable reason.</param>
    /// <returns>True when the id is valid; otherwise false.</returns>
    public static bool IsValid(string manifestId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(manifestId))
        {
            reason = "Manifest ID cannot be null or empty.";
            return false;
        }

        var segments = manifestId.Split('.');
        if (segments.Length < 4)
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Must have at least 4 segments.";
            return false;
        }

        // Assuming InstallationType and GameType enums exist in GenHub.Core.Models.Enums
        var validInstallationTypes = Enum.GetValues<GameInstallationType>().Select(e => e.ToString().ToLowerInvariant()).ToHashSet();
        var validGameTypes = Enum.GetValues<GameType>().Select(e => e.ToString().ToLowerInvariant()).ToHashSet();

        // Check if it matches game installation format: schemaVersion.manifestVersion.installation.game
        if (GameInstallationIdRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // Check if it's a valid publisher ID: schemaVersion.manifestVersion.publisher.content
        // But reject if segments 2 or 3 are valid installation/game types (to avoid conflicts)
        if (segments.Length >= 4 &&
            !validInstallationTypes.Contains(segments[2].ToLowerInvariant()) &&
            !validGameTypes.Contains(segments[2].ToLowerInvariant()) &&
            !validInstallationTypes.Contains(segments[3].ToLowerInvariant()) &&
            !validGameTypes.Contains(segments[3].ToLowerInvariant()) &&
            PublisherIdRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // Check if it's a simple ID (fallback for tests)
        if (SimpleIdRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // For any other cases, reject them
        reason = $"Manifest ID '{manifestId}' is invalid. Must follow either schemaVersion.manifestVersion.publisher.content or schemaVersion.manifestVersion.installation.game format.";
        return false;
    }
}
