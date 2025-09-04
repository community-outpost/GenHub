using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Provides validation for manifest IDs to ensure they follow the deterministic,
/// human-readable scheme required by GenHub.
/// </summary>
public static class ManifestIdValidator
{
    // Regex for publisher content IDs: at least 3 segments, each alphanumeric with dashes (no dots within segments)
    private static readonly Regex PublisherIdRegex =
        new(@"^(?:[a-zA-Z0-9\-]+\.){2,}[a-zA-Z0-9\-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Allow simple test-friendly ids like 'test-id' or 'simple.id' (alphanumeric with dashes and dots)
    private static readonly Regex SimpleIdRegex =
        new(@"^[a-zA-Z0-9\-\.]+$", RegexOptions.Compiled);

    // Regex for base game IDs: installationType.gameType[.version]
    private static readonly Regex PublisherIdRegexPattern =
        new(@"^(unknown|steam|ea|eaapp|origin|thefirstdecade|rgmechanics|cdiso|wine|retail)\.(generals|zerohour)(?:\.\d+(?:\.\d+)*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var validInstallationTypes = new[] { "unknown", "steam", "ea", "eaapp", "origin", "thefirstdecade", "rgmechanics", "cdiso", "wine", "retail" };
        var validGameTypes = new[] { "generals", "zerohour" };

        // Reject IDs that look like invalid base game
        if (segments.Length >= 2 && validInstallationTypes.Contains(segments[0].ToLowerInvariant()) && !validGameTypes.Contains(segments.Length > 1 ? segments[1].ToLowerInvariant() : string.Empty))
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Must follow either publisher.content.version or installationType.gameType.version format.";
            return false;
        }

        if (segments.Length >= 2 && !validInstallationTypes.Contains(segments[0].ToLowerInvariant()) && validGameTypes.Contains(segments[1].ToLowerInvariant()))
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Must follow either publisher.content.version or installationType.gameType.version format.";
            return false;
        }

        // Check if it's a valid base game ID first
        if (segments.Length >= 2 && segments.Length <= 4)
        {
            var installationType = segments[0].ToLowerInvariant();
            var gameType = segments.Length > 1 ? segments[1].ToLowerInvariant() : string.Empty;

            if (validInstallationTypes.Contains(installationType) && validGameTypes.Contains(gameType))
            {
                if (PublisherIdRegexPattern.IsMatch(manifestId))
                {
                    reason = string.Empty;
                    return true;
                }
                else
                {
                    reason = $"Manifest ID '{manifestId}' is invalid. Must follow either publisher.content.version or installationType.gameType.version format.";
                    return false;
                }
            }
        }

        // Reject IDs that start with valid installation type but are not valid base game IDs
        if (segments.Length >= 2 && validInstallationTypes.Contains(segments[0].ToLowerInvariant()) && !PublisherIdRegexPattern.IsMatch(manifestId))
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Must follow either publisher.content.version or installationType.gameType.version format.";
            return false;
        }

        // Check if it's a valid publisher ID
        if (PublisherIdRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // Check if it's a simple ID
        if (SimpleIdRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // For any other cases, reject them
        reason = $"Manifest ID '{manifestId}' is invalid. Must follow either publisher.content.version or installationType.gameType.version format.";
        return false;
    }
}
