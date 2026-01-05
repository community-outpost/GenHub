using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for creating game profiles for game clients.
/// Centralizes profile creation logic for both scan-for-games and content downloads.
/// </summary>
public class GameClientProfileService(
    IGameProfileManager profileManager,
    IGameInstallationService installationService,
    IConfigurationProviderService configService,
    Core.Interfaces.Manifest.IContentManifestPool manifestPool,
    ILogger<GameClientProfileService> logger) : IGameClientProfileService
{
    /// <inheritdoc />
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileForGameClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        string? iconPath = null,
        string? coverPath = null,
        string? themeColor = null,
        CancellationToken cancellationToken = default)
    {
        if (installation == null)
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Installation cannot be null");
        }

        if (gameClient == null)
        {
            logger.LogWarning("GameClient is null for installation {InstallationId}", installation.Id);
            return ProfileOperationResult<GameProfile>.CreateFailure("GameClient cannot be null");
        }

        try
        {
            var profileName = $"{installation.InstallationType} {gameClient.Name}";

            if (await ProfileExistsAsync(profileName, installation.Id, gameClient.Id, cancellationToken))
            {
                logger.LogDebug(
                    "Profile already exists for {InstallationType} {GameClientName}",
                    installation.InstallationType,
                    gameClient.Name);
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile already exists");
            }

            var preferredStrategy = configService.GetDefaultWorkspaceStrategy();

            // Resolve dependencies from the GameClient's manifest
            // We pass null for acquiredManifest because we assume the client is already resolved and in the pool
            var enabledContentIds = await ResolveEnabledContentAsync(
                gameClient,
                installation,
                null,
                cancellationToken);

            logger.LogInformation(
                "Resolved {Count} enabled content IDs for {GameClientName}: [{ContentIds}]",
                enabledContentIds.Count,
                gameClient.Name,
                string.Join(", ", enabledContentIds));

            var createRequest = new CreateProfileRequest
            {
                Name = profileName,
                GameInstallationId = installation.Id,
                GameClientId = gameClient.Id,
                GameClient = gameClient,
                Description = $"Auto-created profile for {installation.InstallationType} {gameClient.Name}",
                PreferredStrategy = preferredStrategy,
                EnabledContentIds = enabledContentIds,
                ThemeColor = themeColor ?? GetThemeColorForGameType(gameClient.GameType, gameClient),
                IconPath = !string.IsNullOrEmpty(iconPath) ? iconPath : GetIconPathForGame(gameClient.GameType),
                CoverPath = !string.IsNullOrEmpty(coverPath) ? coverPath : GetCoverPathForGame(gameClient.GameType, gameClient),
                UseSteamLaunch = installation.InstallationType == GameInstallationType.Steam,
            };

            var profileResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);

            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation(
                    "Successfully created profile '{ProfileName}' for {InstallationType} {GameClientName}",
                    profileResult.Data.Name,
                    installation.InstallationType,
                    gameClient.Name);
            }
            else
            {
                var errors = string.Join(", ", profileResult.Errors);
                logger.LogWarning(
                    "Failed to create profile for {InstallationType} {GameClientName}: {Errors}",
                    installation.InstallationType,
                    gameClient.Name,
                    errors);
            }

            return profileResult;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating profile for {InstallationType} {GameClientName}",
                installation.InstallationType,
                gameClient.Name);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Error creating profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<List<ProfileOperationResult<GameProfile>>> CreateProfilesForGameClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        string? iconPath = null,
        string? coverPath = null,
        string? themeColor = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProfileOperationResult<GameProfile>>();

        if (installation == null)
        {
            results.Add(ProfileOperationResult<GameProfile>.CreateFailure("Installation cannot be null"));
            return results;
        }

        if (gameClient == null)
        {
            logger.LogWarning("GameClient is null for installation {InstallationId}", installation.Id);
            results.Add(ProfileOperationResult<GameProfile>.CreateFailure("GameClient cannot be null"));
            return results;
        }

        try
        {
            // With the new detection pipeline, GameClients are already resolved to valid variants.
            // We no longer need to handle "placeholders" that expand into multiple profiles.
            // Each content variant (e.g., 30Hz, 60Hz) is detected as a separate GameClient.
            var singleResult = await CreateProfileForGameClientAsync(
                installation,
                gameClient,
                iconPath,
                coverPath,
                themeColor,
                cancellationToken);
            results.Add(singleResult);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating profiles for {InstallationType} {GameClientName}",
                installation.InstallationType,
                gameClient.Name);
            results.Add(ProfileOperationResult<GameProfile>.CreateFailure($"Error creating profiles: {ex.Message}"));
            return results;
        }
    }

    /// <inheritdoc />
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileFromManifestAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        if (manifest == null)
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Manifest cannot be null");
        }

        if (manifest.ContentType != ContentType.GameClient)
        {
            logger.LogDebug("Skipping auto-profile creation for non-GameClient content: {ContentType}", manifest.ContentType);
            return ProfileOperationResult<GameProfile>.CreateFailure("Not a GameClient manifest");
        }

        try
        {
            if (await ProfileExistsForGameClientAsync(manifest.Id.Value, cancellationToken))
            {
                logger.LogDebug("Profile already exists for manifest {ManifestId}", manifest.Id);
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile already exists for this manifest");
            }

            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                logger.LogWarning("Failed to get installations for manifest profile creation");
                return ProfileOperationResult<GameProfile>.CreateFailure("Could not retrieve game installations");
            }

            var matchingInstallation = installationsResult.Data.FirstOrDefault(i =>
                (manifest.TargetGame == GameType.Generals && i.HasGenerals) ||
                (manifest.TargetGame == GameType.ZeroHour && i.HasZeroHour));

            if (matchingInstallation == null)
            {
                logger.LogWarning(
                    "No matching installation found for manifest {ManifestId} targeting {TargetGame}",
                    manifest.Id,
                    manifest.TargetGame);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"No installation found for {manifest.TargetGame}");
            }

            // Create a GameClient object from the manifest
            // Extract executable path from manifest files
            var executableFile = manifest.Files?.FirstOrDefault(f =>
                f.RelativePath != null && f.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (executableFile == null)
            {
                logger.LogWarning("Manifest {ManifestId} has no executable file", manifest.Id);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    "Manifest does not contain an executable file");
            }

            // Derive installation path from matching installation based on target game
            var installationPath = manifest.TargetGame == GameType.ZeroHour
                ? matchingInstallation.ZeroHourPath
                : matchingInstallation.GeneralsPath;

            if (string.IsNullOrEmpty(installationPath))
            {
                logger.LogWarning(
                    "Installation path not found for manifest {ManifestId} targeting {TargetGame} in installation {InstallationId}",
                    manifest.Id,
                    manifest.TargetGame,
                    matchingInstallation.Id);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Installation path not found for {manifest.TargetGame}");
            }

            var gameClient = new GameClient
            {
                Id = manifest.Id.Value,
                Name = manifest.Name,
                Version = manifest.Version,
                GameType = manifest.TargetGame,
                SourceType = ContentType.GameClient,
                ExecutablePath = Path.Combine(installationPath, executableFile.RelativePath),
                WorkingDirectory = installationPath,
                InstallationId = matchingInstallation.Id,
            };

            return await CreateProfileForGameClientAsync(
                matchingInstallation,
                gameClient,
                manifest.Metadata.IconUrl,
                manifest.Metadata.CoverUrl,
                manifest.Metadata.ThemeColor,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profile from manifest {ManifestId}", manifest.Id);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Error creating profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ProfileExistsForGameClientAsync(
        string gameClientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(gameClientId))
        {
            return false;
        }

        try
        {
            var profilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
            if (!profilesResult.Success || profilesResult.Data == null)
            {
                return false;
            }

            return profilesResult.Data.Any(p =>
                p.GameClient != null &&
                p.GameClient.Id.Equals(gameClientId, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking if profile exists for game client {GameClientId}", gameClientId);
            return false;
        }
    }

    /// <summary>
    /// Gets a fallback installation ID when manifest resolution fails.
    /// </summary>
    private static string? GetFallbackInstallationId(GameInstallation installation, GameType gameType)
    {
        // Simple fallback: find any standard game client for the type
        var baseGameClient = installation.AvailableGameClients
            .FirstOrDefault(c => c.GameType == gameType && IsStandardGameClient(c));

        if (baseGameClient != null)
        {
            var version = CalculateManifestVersion(baseGameClient);
            return ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, version);
        }

        return null;
    }

    private static bool IsStandardGameClient(GameClient client)
    {
        // A standard game client is one that isn't from a special provider
        // Check for known publisher markers in ID
        return !client.Id.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) &&
               !client.Id.Contains(SuperHackersConstants.PublisherId, StringComparison.OrdinalIgnoreCase);
    }

    private static int CalculateManifestVersion(GameClient gameClient)
    {
        if (string.IsNullOrEmpty(gameClient.Version) ||
            gameClient.Version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
            gameClient.Version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
            gameClient.Version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            // If version is unknown, use the default version for the game type (1.04/1.08)
            // This ensures we match the ID generated during initial scan for standard installations
            return GetDefaultVersion(gameClient.GameType);
        }

        return GameVersionHelper.NormalizeVersion(gameClient.Version);
    }

    private static int GetDefaultVersion(GameType gameType)
    {
        var fallbackVersion = gameType == GameType.ZeroHour
            ? ManifestConstants.ZeroHourManifestVersion
            : ManifestConstants.GeneralsManifestVersion;

        return GameVersionHelper.NormalizeVersion(fallbackVersion);
    }

    private static string? GetThemeColorForGameType(GameType gameType, GameClient? gameClient = null)
    {
        if (gameClient != null)
        {
            // TheSuperHackers gets special colors
            if (gameClient.PublisherType == PublisherTypeConstants.TheSuperHackers)
            {
                return gameType == GameType.ZeroHour ? SuperHackersConstants.ZeroHourThemeColor : SuperHackersConstants.GeneralsThemeColor;
            }

            // GeneralsOnline gets dark blue
            if (gameClient.PublisherType == PublisherTypeConstants.GeneralsOnline)
            {
                return GeneralsOnlineConstants.ThemeColor;
            }

            // CommunityOutpost gets green
            if (gameClient.PublisherType == CommunityOutpostConstants.PublisherType)
            {
                return CommunityOutpostConstants.ThemeColor;
            }
        }

        // For auto-detected profiles without publisher type, return null to use manifest color
        // Manifest factories will set their own colors (CommunityOutpost=green, GeneralsOnline=dark blue)
        return null;
    }

    private static string GetIconPathForGame(GameType gameType)
    {
        var gameIcon = gameType == GameType.Generals
            ? UriConstants.GeneralsIconFilename
            : UriConstants.ZeroHourIconFilename;

        return $"{UriConstants.IconsBasePath}/{gameIcon}";
    }

    private static string GetCoverPathForGame(GameType gameType, GameClient? gameClient = null)
    {
        if (gameClient != null)
        {
            if (gameClient.PublisherType == PublisherTypeConstants.TheSuperHackers)
            {
                return $"{UriConstants.CoversBasePath}/china-poster.png";
            }

            if (gameClient.PublisherType == CommunityOutpostConstants.PublisherType)
            {
                return $"{UriConstants.CoversBasePath}/gla-poster.png";
            }

            if (gameClient.PublisherType == PublisherTypeConstants.GeneralsOnline)
            {
                return $"{UriConstants.CoversBasePath}/usa-poster.png";
            }
        }

        var gameCover = gameType == GameType.Generals
            ? UriConstants.GeneralsCoverFilename
            : UriConstants.ZeroHourCoverFilename;

        return $"{UriConstants.CoversBasePath}/{gameCover}";
    }

    /// <summary>
    /// Resolves the enabled content IDs for a game client based on its manifest dependencies.
    /// </summary>
    private async Task<List<string>> ResolveEnabledContentAsync(
        GameClient gameClient,
        GameInstallation installation,
        ContentManifest? providedManifest,
        CancellationToken cancellationToken)
    {
        var enabledContentIds = new List<string> { gameClient.Id };

        // Use provided manifest if available, otherwise try to get from pool
        ContentManifest? manifest = providedManifest;
        if (manifest == null && manifestPool != null)
        {
            var manifestResult = await manifestPool.GetManifestAsync(
                ManifestId.Create(gameClient.Id), cancellationToken);
            manifest = manifestResult.Data;
        }

        // If no manifest available, use fallback dependencies
        if (manifest == null)
        {
            logger.LogWarning(
                "Could not retrieve manifest for {GameClientId}, falling back to default dependencies",
                gameClient.Id);

            // Fallback: Add game installation dependency based on game type
            var fallbackInstallId = GetFallbackInstallationId(installation, gameClient.GameType);
            if (!string.IsNullOrEmpty(fallbackInstallId))
            {
                enabledContentIds.Add(fallbackInstallId);
            }

            return enabledContentIds;
        }

        // Process each dependency from the manifest
        if (manifest.Dependencies != null && manifest.Dependencies.Count > 0)
        {
            foreach (var dependency in manifest.Dependencies)
            {
                var resolvedId = await ResolveDependencyToContentIdAsync(dependency, installation, gameClient.GameType, cancellationToken);
                if (!string.IsNullOrEmpty(resolvedId) && !enabledContentIds.Contains(resolvedId))
                {
                    enabledContentIds.Add(resolvedId);
                    logger.LogDebug(
                        "Resolved dependency '{DependencyName}' to content ID: {ContentId}",
                        dependency.Name,
                        resolvedId);
                }
            }
        }
        else
        {
            logger.LogDebug(
                "Manifest {ManifestId} has no dependencies defined",
                manifest.Id.Value);

            // Fallback: Add game installation dependency based on game type
            var fallbackInstallId = GetFallbackInstallationId(installation, gameClient.GameType);
            if (!string.IsNullOrEmpty(fallbackInstallId))
            {
                enabledContentIds.Add(fallbackInstallId);
            }
        }

        return enabledContentIds;
    }

    /// <summary>
    /// Resolves a content dependency to an actual content ID by querying the manifest pool.
    /// </summary>
    private async Task<string?> ResolveDependencyToContentIdAsync(
        ContentDependency dependency,
        GameInstallation installation,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        if (dependency.DependencyType == ContentType.GameInstallation)
        {
            // For game installation dependencies, query the manifest pool for the actual manifest
            var targetGameType = dependency.CompatibleGameTypes?.FirstOrDefault() ?? gameType;

            // Find the base game client for the target game type to calculate version
            var baseGameClient = installation.AvailableGameClients
                .FirstOrDefault(c => c.GameType == targetGameType && IsStandardGameClient(c));

            if (baseGameClient == null)
            {
                logger.LogWarning(
                    "Could not find base game client for {GameType} to resolve dependency {DependencyName}",
                    targetGameType,
                    dependency.Name);
                return null;
            }

            // Generate the expected GameInstallation manifest ID
            var version = CalculateManifestVersion(baseGameClient);
            var expectedInstallId = ManifestIdGenerator.GenerateGameInstallationId(
                installation, targetGameType, version);

            // Verify this manifest actually exists in the pool
            var manifestResult = await manifestPool.GetManifestAsync(
                ManifestId.Create(expectedInstallId), cancellationToken);

            if (manifestResult.Success && manifestResult.Data != null)
            {
                logger.LogDebug(
                    "Resolved GameInstallation dependency '{DependencyName}' to manifest ID: {ManifestId}",
                    dependency.Name,
                    expectedInstallId);
                return expectedInstallId;
            }

            logger.LogWarning(
                "GameInstallation manifest {ManifestId} for {GameType} not found in pool for dependency {DependencyName}",
                expectedInstallId,
                targetGameType,
                dependency.Name);
            return null;
        }
        else
        {
            // For non-installation dependencies (MapPack, etc.), use the dependency ID directly
            return dependency.Id.Value;
        }
    }

    private async Task<bool> ProfileExistsAsync(
        string profileName,
        string installationId,
        string gameClientId,
        CancellationToken cancellationToken)
    {
        var profilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!profilesResult.Success || profilesResult.Data == null)
        {
            return false;
        }

        var profileExists = profilesResult.Data.Any(p =>
            p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
            p.GameInstallationId.Equals(installationId, StringComparison.OrdinalIgnoreCase));

        if (profileExists)
        {
            return true;
        }

        return profilesResult.Data.Any(p =>
            p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
            p.GameClient != null &&
            p.GameClient.Id.Equals(gameClientId, StringComparison.OrdinalIgnoreCase));
    }
}
