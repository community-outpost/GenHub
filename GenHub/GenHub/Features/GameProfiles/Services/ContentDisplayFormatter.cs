using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Extensions.Enums;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using System;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for formatting content items for display in the UI.
/// Follows Single Responsibility Principle by centralizing all display formatting logic.
/// </summary>
public sealed class ContentDisplayFormatter(IGameClientHashRegistry hashRegistry) : IContentDisplayFormatter
{
    private const string CommunityPatchIdentifier = "CommunityPatch";
    private const string VersionPrefix = "v";

    /// <inheritdoc/>
    public GenHub.Core.Models.Content.ContentDisplayItem CreateDisplayItem(ContentManifest manifest, bool isEnabled = false)
    {
        var publisher = GetPublisherFromManifest(manifest);
        var installationType = GetInstallationTypeFromManifest(manifest);
        var normalizedVersion = NormalizeVersion(manifest.Version);
        var displayName = BuildDisplayName(manifest.TargetGame, normalizedVersion, manifest.Name);

        return new GenHub.Core.Models.Content.ContentDisplayItem
        {
            Id = manifest.Id.Value,
            ManifestId = manifest.Id.Value,
            DisplayName = displayName,
            ContentType = manifest.ContentType,
            GameType = manifest.TargetGame,
            InstallationType = installationType,
            Publisher = publisher,
            Version = normalizedVersion,
            IsEnabled = isEnabled,
            Manifest = manifest,
        };
    }

    /// <inheritdoc/>
    public GenHub.Core.Models.Content.ContentDisplayItem CreateDisplayItemFromInstallation(
        GameInstallation installation,
        GameClient gameClient,
        ManifestId manifestId,
        bool isEnabled = false)
    {
        var publisherName = GetPublisherFromInstallationType(installation.InstallationType);
        var normalizedVersion = NormalizeVersion(gameClient.Version);
        var displayName = BuildDisplayName(gameClient.GameType, normalizedVersion);

        return new GenHub.Core.Models.Content.ContentDisplayItem
        {
            Id = manifestId.Value,
            ManifestId = manifestId.Value,
            DisplayName = displayName,
            ContentType = ContentType.GameInstallation,
            GameType = gameClient.GameType,
            InstallationType = installation.InstallationType,
            Publisher = publisherName,
            Version = normalizedVersion,
            IsEnabled = isEnabled,
            SourceId = installation.Id,
            GameClientId = gameClient.Id,
        };
    }

    /// <inheritdoc/>
    public string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var trimmedVersion = version.Trim();

        // Return empty for special case versions
        if (trimmedVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            trimmedVersion.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
            trimmedVersion.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase) ||
            trimmedVersion.Contains("Automatically", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Handle Auto-Updated versions (GeneralsOnline) - return empty string
        if (trimmedVersion.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Handle auto-detected GeneralsOnline clients - return empty string to avoid showing "vAutomatically added"
        if (trimmedVersion.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Try to resolve hash-based versions (e.g., from GameClientHashRegistry)
        var (detectedGameType, hashVersion) = hashRegistry.GetGameInfoFromHash(trimmedVersion);
        if (detectedGameType != GameType.Unknown && !string.IsNullOrEmpty(hashVersion))
        {
            return hashVersion;
        }

        // Remove 'v' prefix if present (case-insensitive)
        if (trimmedVersion.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedVersion.Substring(VersionPrefix.Length).Trim();
        }

        return trimmedVersion;
    }

    /// <inheritdoc/>
    public string BuildDisplayName(GameType gameType, string normalizedVersion, string? name = null)
    {
        var gameShortName = GetGameTypeDisplayName(gameType, useShortName: true);

        // If name is provided, use it as the primary display name
        if (!string.IsNullOrWhiteSpace(name))
        {
            // For GeneralsOnline clients and other third-party clients, the name is already descriptive
            // (e.g., "GeneralsOnline 30Hz", "GeneralsOnline 60Hz", "GeneralsOnline")
            // Don't append version if it's empty or already in the name
            if (string.IsNullOrWhiteSpace(normalizedVersion) ||
                name.Contains(normalizedVersion, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            var formattedVersion = FormatVersion(normalizedVersion);

            // Check if the name already contains the formatted version
            if (name.Contains(formattedVersion, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            return $"{name} {formattedVersion}";
        }

        // No name provided, use game type + version
        var versionDisplay = FormatVersion(normalizedVersion);

        // If no version, just return game name
        if (string.IsNullOrWhiteSpace(versionDisplay))
        {
            return gameShortName;
        }

        return $"{gameShortName} {versionDisplay}";
    }

    /// <inheritdoc/>
    public string FormatVersion(string version)
    {
        var normalizedVersion = NormalizeVersion(version);

        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return string.Empty;
        }

        return IsCommunityVersion(normalizedVersion)
            ? normalizedVersion
            : $"{VersionPrefix}{normalizedVersion}";
    }

    /// <inheritdoc/>
#pragma warning disable CS0618 // Type or member is obsolete
    public string FormatVersion(string version, ContentType contentType)
    {
        // contentType parameter is ignored - kept only for backward compatibility
        return FormatVersion(version);
    }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <inheritdoc/>
    public string GetGameTypeDisplayName(GameType gameType, bool useShortName = false)
    {
        if (useShortName)
        {
            return gameType switch
            {
                GameType.ZeroHour => GameClientName.ZeroHour.GetShortName(),
                GameType.Generals => GameClientName.Generals.GetShortName(),
                _ => gameType.ToString(),
            };
        }

        return gameType switch
        {
            GameType.Generals => GameClientName.Generals.GetFullName(),
            GameType.ZeroHour => GameClientName.ZeroHour.GetFullName(),
            _ => gameType.ToString(),
        };
    }

    /// <inheritdoc/>
    public string GetContentTypeDisplayName(ContentType contentType)
    {
        return contentType.GetDisplayName();
    }

    /// <inheritdoc/>
    public string GetPublisherFromInstallationType(GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => Publisher.Steam.GetDisplayName(),
            GameInstallationType.EaApp => Publisher.EaApp.GetDisplayName(),
            GameInstallationType.TheFirstDecade => Publisher.TheFirstDecade.GetDisplayName(),
            GameInstallationType.Wine => Publisher.Wine.GetDisplayName(),
            GameInstallationType.CDISO => Publisher.CdRom.GetDisplayName(),
            GameInstallationType.Retail => Publisher.Retail.GetDisplayName(),
            _ => Publisher.Unknown.GetDisplayName(),
        };
    }

    /// <inheritdoc/>
    public string GetPublisherFromManifest(ContentManifest manifest)
    {
        // Priority 1: Use manifest.Publisher.Name if explicitly set
        if (manifest.Publisher?.Name is { } publisherName && !string.IsNullOrWhiteSpace(publisherName))
        {
            return publisherName;
        }

        // Priority 2: Derive from installation type (for official game installations)
        var installationType = GetInstallationTypeFromManifest(manifest);
        if (installationType != GameInstallationType.Retail)
        {
            // If we detected a specific installation type (Steam, EA, etc.), use its display name
            return installationType.GetDisplayName();
        }

        // Priority 3: Parse publisher from manifest name as fallback
        var lowerName = manifest.Name.ToLowerInvariant();

        if (lowerName.Contains("steam")) return Publisher.Steam.GetDisplayName();
        if (lowerName.Contains("ea") || lowerName.Contains("origin")) return Publisher.EaApp.GetDisplayName();
        if (lowerName.Contains("generalsonline")) return Publisher.GeneralsOnline.GetDisplayName();
        if (lowerName.Contains("thesuperhackers") || lowerName.Contains("superhacker")) return Publisher.SuperHackers.GetDisplayName();
        if (lowerName.Contains("cnclabs")) return Publisher.CncLabs.GetDisplayName();

        // Priority 4: Default to installation type display name (handles Retail and Unknown)
        return installationType.GetDisplayName();
    }

    /// <inheritdoc/>
    public GameInstallationType GetInstallationTypeFromManifest(ContentManifest manifest)
    {
        var lowerName = manifest.Name.ToLowerInvariant();

        if (lowerName.Contains("steam")) return GameInstallationType.Steam;
        if (lowerName.Contains("ea") || lowerName.Contains("origin")) return GameInstallationType.EaApp;
        if (lowerName.Contains("tfd") || lowerName.Contains("firstdecade")) return GameInstallationType.TheFirstDecade;
        if (lowerName.Contains("wine") || lowerName.Contains("proton")) return GameInstallationType.Wine;

        return GameInstallationType.Retail;
    }

    private static bool IsCommunityVersion(string version)
    {
        return version.Contains(CommunityPatchIdentifier, StringComparison.OrdinalIgnoreCase);
    }
}
