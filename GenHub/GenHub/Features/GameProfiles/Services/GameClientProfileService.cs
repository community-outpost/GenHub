using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Holds both the GameClient and its manifest after acquisition.
/// This allows passing the manifest directly for dependency resolution
/// instead of re-querying the manifest pool.
/// </summary>
/// <param name="client">The game client.</param>
/// <param name="manifest">The content manifest.</param>
internal record AcquiredGameClient(GameClient client, ContentManifest manifest)
{
    /// <summary>
    /// Gets the game client.
    /// </summary>
    public GameClient Client { get; } = client;

    /// <summary>
    /// Gets the content manifest.
    /// </summary>
    public ContentManifest Manifest { get; } = manifest;
}

/// <summary>
/// Service for creating game profiles for game clients.
/// Centralizes profile creation logic for both scan-for-games and content downloads.
/// </summary>
public class GameClientProfileService(
    IGameProfileManager profileManager,
    IGameInstallationService installationService,
    IConfigurationProviderService configService,
    IContentAcquisitionService acquisitionService,
    Core.Interfaces.Manifest.IContentManifestPool manifestPool,
    ILogger<GameClientProfileService> logger) : IGameClientProfileService
{
    /// <inheritdoc />
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileForGameClientAsync(
        GameInstallation installation,
        GameClient gameClient,
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
            // Track the manifest if we acquire one - this is passed to dependency resolution
            ContentManifest? acquiredManifest = null;

            if (IsGeneralsOnlinePlaceholder(gameClient.Id))
            {
                logger.LogInformation(
                    "Detected GeneralsOnline placeholder: {PlaceholderId}",
                    gameClient.Id);

                // Acquire GeneralsOnline content - this creates 30Hz, 60Hz, and MapPack manifests in pool
                var goResult = await AcquireGeneralsOnlineClientAsync(gameClient, installation, cancellationToken);
                if (!goResult.Success || goResult.Data == null)
                {
                    logger.LogWarning(
                        "Failed to acquire GeneralsOnline content: {Errors}",
                        string.Join(", ", goResult.Errors));
                    return ProfileOperationResult<GameProfile>.CreateFailure(
                        $"Failed to acquire GeneralsOnline content: {string.Join(", ", goResult.Errors)}");
                }

                // After acquisition, create profiles for ALL GeneralsOnline variants (30Hz + 60Hz)
                var createdProfiles = await CreateProfilesForAllGeneralsOnlineVariantsAsync(
                    installation,
                    gameClient,
                    goResult.Data.Manifest,
                    cancellationToken);

                // Return the first successfully created profile (or the failure if none created)
                if (createdProfiles.Count == 0)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure("No GeneralsOnline profiles could be created");
                }

                // Return the first created profile to satisfy the interface contract
                // The other profiles were already created and will show up in UI
                var firstSuccess = createdProfiles.FirstOrDefault(p => p.Success && p.Data != null);
                if (firstSuccess?.Data != null)
                {
                    logger.LogInformation(
                        "Created {Count} GeneralsOnline profiles: [{ProfileNames}]",
                        createdProfiles.Count(p => p.Success),
                        string.Join(", ", createdProfiles.Where(p => p.Success && p.Data != null).Select(p => p.Data!.Name)));
                    return firstSuccess;
                }

                return createdProfiles.First();
            }

            if (IsSuperHackersPlaceholder(gameClient.Id))
            {
                logger.LogInformation(
                    "Detected SuperHackers placeholder: {PlaceholderId}",
                    gameClient.Id);

                // Acquire SuperHackers content - this creates Generals and ZeroHour manifests in pool
                var shResult = await AcquireSuperHackersClientAsync(gameClient, installation, cancellationToken);
                if (!shResult.Success || shResult.Data == null)
                {
                    logger.LogWarning(
                        "Failed to acquire SuperHackers content: {Errors}",
                        string.Join(", ", shResult.Errors));
                    return ProfileOperationResult<GameProfile>.CreateFailure(
                        $"Failed to acquire SuperHackers content: {string.Join(", ", shResult.Errors)}");
                }

                // After acquisition, create profiles for ALL SuperHackers game types (Generals + ZeroHour)
                var createdProfiles = await CreateProfilesForAllSuperHackersVariantsAsync(
                    installation,
                    gameClient,
                    shResult.Data.Manifest,
                    cancellationToken);

                // Return the first successfully created profile (or the failure if none created)
                if (createdProfiles.Count == 0)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure("No SuperHackers profiles could be created");
                }

                var firstSuccess = createdProfiles.FirstOrDefault(p => p.Success && p.Data != null);
                if (firstSuccess?.Data != null)
                {
                    logger.LogInformation(
                        "Created {Count} SuperHackers profiles: [{ProfileNames}]",
                        createdProfiles.Count(p => p.Success),
                        string.Join(", ", createdProfiles.Where(p => p.Success && p.Data != null).Select(p => p.Data!.Name)));
                    return firstSuccess;
                }

                return createdProfiles.First();
            }

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

            // Resolve dependencies from the GameClient's manifest - pass acquired manifest directly
            var enabledContentIds = await ResolveEnabledContentAsync(
                gameClient,
                installation,
                acquiredManifest,
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
                ThemeColor = GetThemeColorForGameType(gameClient.GameType),
                IconPath = GetIconPathForGame(gameClient.GameType),
            };

            var profileResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);

            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation(
                    "Successfully created profile '{ProfileName}' for {InstallationType} {GameClientName}",
                    profileResult.Data.Name,
                    installation.InstallationType,
                    gameClient.Name);

                // NOTE: Sibling variant profiles (e.g., 60Hz) are detected and handled separately
                // by GameClientDetector, so we don't need to create them here.
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
            // Handle GeneralsOnline multi-variant content
            if (IsGeneralsOnlinePlaceholder(gameClient.Id))
            {
                logger.LogInformation(
                    "Detected GeneralsOnline placeholder for multi-profile creation: {PlaceholderId}",
                    gameClient.Id);

                var goResult = await AcquireGeneralsOnlineClientAsync(gameClient, installation, cancellationToken);
                if (!goResult.Success || goResult.Data == null)
                {
                    logger.LogWarning(
                        "Failed to acquire GeneralsOnline content: {Errors}",
                        string.Join(", ", goResult.Errors));
                    results.Add(ProfileOperationResult<GameProfile>.CreateFailure(
                        $"Failed to acquire GeneralsOnline content: {string.Join(", ", goResult.Errors)}"));
                    return results;
                }

                // Create profiles for ALL variants and return ALL of them
                var createdProfiles = await CreateProfilesForAllGeneralsOnlineVariantsAsync(
                    installation,
                    gameClient,
                    goResult.Data.Manifest,
                    cancellationToken);

                results.AddRange(createdProfiles);

                logger.LogInformation(
                    "Created {Count} GeneralsOnline profiles: [{ProfileNames}]",
                    createdProfiles.Count(p => p.Success),
                    string.Join(", ", createdProfiles.Where(p => p.Success && p.Data != null).Select(p => p.Data!.Name)));

                return results;
            }

            // Handle SuperHackers multi-variant content
            if (IsSuperHackersPlaceholder(gameClient.Id))
            {
                logger.LogInformation(
                    "Detected SuperHackers placeholder for multi-profile creation: {PlaceholderId}",
                    gameClient.Id);

                var shResult = await AcquireSuperHackersClientAsync(gameClient, installation, cancellationToken);
                if (!shResult.Success || shResult.Data == null)
                {
                    logger.LogWarning(
                        "Failed to acquire SuperHackers content: {Errors}",
                        string.Join(", ", shResult.Errors));
                    results.Add(ProfileOperationResult<GameProfile>.CreateFailure(
                        $"Failed to acquire SuperHackers content: {string.Join(", ", shResult.Errors)}"));
                    return results;
                }

                // Create profiles for ALL game types and return ALL of them
                var createdProfiles = await CreateProfilesForAllSuperHackersVariantsAsync(
                    installation,
                    gameClient,
                    shResult.Data.Manifest,
                    cancellationToken);

                results.AddRange(createdProfiles);

                logger.LogInformation(
                    "Created {Count} SuperHackers profiles: [{ProfileNames}]",
                    createdProfiles.Count(p => p.Success),
                    string.Join(", ", createdProfiles.Where(p => p.Success && p.Data != null).Select(p => p.Data!.Name)));

                return results;
            }

            // Single-variant content: create one profile and return it in a list
            var singleResult = await CreateProfileForGameClientAsync(installation, gameClient, cancellationToken);
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

            var gameClient = new GameClient
            {
                Id = manifest.Id.Value,
                Name = manifest.Name,
                Version = manifest.Version,
                GameType = manifest.TargetGame,
            };

            return await CreateProfileForGameClientAsync(matchingInstallation, gameClient, cancellationToken);
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
        var baseGameClient = installation.AvailableGameClients
            .FirstOrDefault(c => c.GameType == gameType &&
                                 !IsGeneralsOnlinePlaceholder(c.Id) &&
                                 !IsSuperHackersPlaceholder(c.Id) &&
                                 !IsGeneralsOnlineManifestId(c.Id) &&
                                 !IsSuperHackersManifestId(c.Id));

        if (baseGameClient != null)
        {
            var version = CalculateManifestVersion(baseGameClient);
            return ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, version);
        }

        return null;
    }

    private static bool IsGeneralsOnlinePlaceholder(string gameClientId) =>
        ManifestIdHelper.IsPlaceholder(gameClientId, PublisherTypeConstants.GeneralsOnline);

    private static bool IsGeneralsOnlineManifestId(string manifestId) =>
        ManifestIdHelper.IsResolved(manifestId, PublisherTypeConstants.GeneralsOnline);

    private static bool IsSuperHackersPlaceholder(string gameClientId) =>
        ManifestIdHelper.IsPlaceholder(gameClientId, SuperHackersConstants.PublisherId);

    private static bool IsSuperHackersManifestId(string manifestId) =>
        ManifestIdHelper.IsResolved(manifestId, SuperHackersConstants.PublisherId);

    private static string? ExtractVariant(string placeholderId)
    {
        if (string.IsNullOrEmpty(placeholderId))
        {
            return null;
        }

        var parts = placeholderId.Split(':');
        return parts.Length >= 3 ? parts[2] : null;
    }

    private static string? GetGeneralsOnlineMapPackId(string gameClientManifestId)
    {
        if (string.IsNullOrEmpty(gameClientManifestId))
        {
            return null;
        }

        var parts = gameClientManifestId.Split('.');
        if (parts.Length < 5)
        {
            return null;
        }

        return $"{parts[0]}.{parts[1]}.{parts[2]}.mappack.{GeneralsOnlineConstants.QuickMatchMapPackSuffix}";
    }

    private static int CalculateManifestVersion(GameClient gameClient)
    {
        if (string.IsNullOrEmpty(gameClient.Version) ||
            gameClient.Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            gameClient.Version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
            gameClient.Version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            var fallbackVersion = gameClient.GameType == GameType.ZeroHour
                ? ManifestConstants.ZeroHourManifestVersion
                : ManifestConstants.GeneralsManifestVersion;

            var normalizedFallback = fallbackVersion.Replace(".", string.Empty);
            return int.TryParse(normalizedFallback, out var v) ? v : 0;
        }

        if (gameClient.Version.Contains('.'))
        {
            var normalized = gameClient.Version.Replace(".", string.Empty);
            return int.TryParse(normalized, out var v) ? v : 0;
        }

        return int.TryParse(gameClient.Version, out var parsed) ? parsed : 0;
    }

    private static string GetThemeColorForGameType(GameType gameType)
    {
        return gameType == GameType.Generals ? "#BD5A0F" : "#1B6575";
    }

    private static string GetIconPathForGame(GameType gameType)
    {
        var gameIcon = gameType == GameType.Generals
            ? UriConstants.GeneralsIconFilename
            : UriConstants.ZeroHourIconFilename;

        return $"{UriConstants.IconsBasePath}/{gameIcon}";
    }

    private static GameClient CreateGameClientFromManifest(
        ContentManifest manifest,
        GameClient placeholderClient,
        GameInstallation installation)
    {
        return new GameClient
        {
            Id = manifest.Id.Value,
            Name = manifest.Name,
            Version = manifest.Version,
            GameType = manifest.TargetGame,
            ExecutablePath = placeholderClient.ExecutablePath,
            WorkingDirectory = placeholderClient.WorkingDirectory,
            InstallationId = installation.Id,
            SourceType = ContentType.GameClient,
        };
    }

    /// <summary>
    /// Resolves the enabled content IDs for a game client based on its manifest dependencies.
    /// </summary>
    /// <param name="gameClient">The game client to resolve dependencies for.</param>
    /// <param name="installation">The game installation.</param>
    /// <param name="providedManifest">Optional manifest from acquisition to avoid re-querying pool.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>List of enabled content IDs including the game client and all its dependencies.</returns>
    private async Task<List<string>> ResolveEnabledContentAsync(
        GameClient gameClient,
        GameInstallation installation,
        ContentManifest? providedManifest,
        CancellationToken cancellationToken)
    {
        var enabledContentIds = new List<string>();

        // Always add the game client itself
        enabledContentIds.Add(gameClient.Id);

        // Use provided manifest if available, otherwise try to get from pool
        ContentManifest? manifest = providedManifest;
        if (manifest == null)
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
                var resolvedId = ResolveDependencyToContentId(dependency, installation, gameClient.GameType);
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
    /// Resolves a content dependency to an actual content ID.
    /// </summary>
    private string? ResolveDependencyToContentId(
        ContentDependency dependency,
        GameInstallation installation,
        GameType gameType)
    {
        if (dependency.DependencyType == ContentType.GameInstallation)
        {
            // For game installation dependencies, resolve to the actual installation manifest ID
            // The dependency may require a different game type (e.g., SuperHackers Generals requires ZeroHour)
            var targetGameType = dependency.CompatibleGameTypes?.FirstOrDefault() ?? gameType;

            // Find the base game client for the target game type
            var baseGameClient = installation.AvailableGameClients
                .FirstOrDefault(c => c.GameType == targetGameType &&
                                     !IsGeneralsOnlinePlaceholder(c.Id) &&
                                     !IsSuperHackersPlaceholder(c.Id) &&
                                     !IsGeneralsOnlineManifestId(c.Id) &&
                                     !IsSuperHackersManifestId(c.Id));

            if (baseGameClient != null)
            {
                var version = CalculateManifestVersion(baseGameClient);
                var installId = ManifestIdGenerator.GenerateGameInstallationId(
                    installation, targetGameType, version);
                return installId;
            }

            logger.LogWarning(
                "Could not find base game client for {GameType} to resolve dependency {DependencyName}",
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

    private async Task<OperationResult<AcquiredGameClient>> AcquireGeneralsOnlineClientAsync(
        GameClient placeholderClient,
        GameInstallation installation,
        CancellationToken cancellationToken)
    {
        var variant = ExtractVariant(placeholderClient.Id);
        if (string.IsNullOrEmpty(variant))
        {
            return OperationResult<AcquiredGameClient>.CreateFailure("Could not extract variant from placeholder ID");
        }

        logger.LogInformation("Acquiring GeneralsOnline content for variant: {Variant}", variant);

        var progress = new Progress<ContentAcquisitionProgress>(p =>
        {
            logger.LogDebug("GeneralsOnline acquisition progress: {Phase} - {Percentage}%", p.Phase, p.ProgressPercentage);
        });

        // Pass the placeholder's working directory as the existing installation path
        // This allows the acquisition service to possibly create a manifest from local files instead of downloading
        var result = await acquisitionService.AcquireGeneralsOnlineContentAsync(
            variant,
            placeholderClient.WorkingDirectory,
            progress,
            cancellationToken);
        if (!result.Success || result.Data == null)
        {
            return OperationResult<AcquiredGameClient>.CreateFailure(result.Errors);
        }

        var manifest = result.Data;
        var gameClient = CreateGameClientFromManifest(manifest, placeholderClient, installation);
        return OperationResult<AcquiredGameClient>.CreateSuccess(new AcquiredGameClient(gameClient, manifest));
    }

    private async Task<OperationResult<AcquiredGameClient>> AcquireSuperHackersClientAsync(
        GameClient placeholderClient,
        GameInstallation installation,
        CancellationToken cancellationToken)
    {
        var gameTypeSuffix = ExtractVariant(placeholderClient.Id);
        if (string.IsNullOrEmpty(gameTypeSuffix))
        {
            return OperationResult<AcquiredGameClient>.CreateFailure("Could not extract game type from placeholder ID");
        }

        var targetGame = gameTypeSuffix.Equals(SuperHackersConstants.GeneralsSuffix, StringComparison.OrdinalIgnoreCase)
            ? GameType.Generals
            : GameType.ZeroHour;

        logger.LogInformation("Acquiring SuperHackers content for game type: {GameType}", targetGame);

        var progress = new Progress<ContentAcquisitionProgress>(p =>
        {
            logger.LogDebug("SuperHackers acquisition progress: {Phase} - {Percentage}%", p.Phase, p.ProgressPercentage);
        });

        // Pass the placeholder's working directory as the existing installation path
        var result = await acquisitionService.AcquireSuperHackersContentAsync(
            targetGame,
            placeholderClient.WorkingDirectory,
            progress,
            cancellationToken);
        if (!result.Success || result.Data == null)
        {
            return OperationResult<AcquiredGameClient>.CreateFailure(result.Errors);
        }

        var manifest = result.Data;
        var gameClient = CreateGameClientFromManifest(manifest, placeholderClient, installation);
        return OperationResult<AcquiredGameClient>.CreateSuccess(new AcquiredGameClient(gameClient, manifest));
    }

    /// <summary>
    /// Creates profiles for ALL GeneralsOnline variants (30Hz, 60Hz) from manifests in the pool.
    /// This ensures that when we detect any GeneralsOnline placeholder and download content,
    /// profiles are created for all available variants.
    /// </summary>
    private async Task<List<ProfileOperationResult<GameProfile>>> CreateProfilesForAllGeneralsOnlineVariantsAsync(
        GameInstallation installation,
        GameClient placeholderClient,
        ContentManifest primaryManifest,
        CancellationToken cancellationToken)
    {
        var results = new List<ProfileOperationResult<GameProfile>>();

        // Get all manifests from pool
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allManifestsResult.Success || allManifestsResult.Data == null)
        {
            logger.LogWarning("Failed to get manifests from pool for GeneralsOnline variant creation");
            results.Add(ProfileOperationResult<GameProfile>.CreateFailure("Could not retrieve manifests from pool"));
            return results;
        }

        // Find all GeneralsOnline GameClient manifests (30Hz and 60Hz)
        var generalsOnlineManifests = allManifestsResult.Data
            .Where(m =>
                m.ContentType == ContentType.GameClient &&
                m.Id.Value.Contains(GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        logger.LogInformation(
            "Found {Count} GeneralsOnline GameClient manifests in pool: [{ManifestIds}]",
            generalsOnlineManifests.Count,
            string.Join(", ", generalsOnlineManifests.Select(m => m.Id.Value)));

        if (generalsOnlineManifests.Count == 0)
        {
            // Fall back to just the primary manifest if pool lookup failed
            generalsOnlineManifests.Add(primaryManifest);
        }

        // Create a profile for each variant
        foreach (var manifest in generalsOnlineManifests)
        {
            var profileName = $"{installation.InstallationType} {manifest.Name}";

            // Check if profile already exists
            if (await ProfileExistsAsync(profileName, installation.Id, manifest.Id.Value, cancellationToken))
            {
                logger.LogDebug(
                    "Profile already exists for {ProfileName}, skipping",
                    profileName);
                continue;
            }

            // Create GameClient from manifest
            var gameClient = CreateGameClientFromManifest(manifest, placeholderClient, installation);

            var preferredStrategy = configService.GetDefaultWorkspaceStrategy();

            // Resolve dependencies for this variant
            var enabledContentIds = await ResolveEnabledContentAsync(
                gameClient,
                installation,
                manifest,
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
                ThemeColor = GetThemeColorForGameType(gameClient.GameType),
                IconPath = GetIconPathForGame(gameClient.GameType),
            };

            var profileResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);
            results.Add(profileResult);

            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation(
                    "Successfully created profile '{ProfileName}' for variant {ManifestId}",
                    profileResult.Data.Name,
                    manifest.Id.Value);
            }
            else
            {
                logger.LogWarning(
                    "Failed to create profile for variant {ManifestId}: {Errors}",
                    manifest.Id.Value,
                    string.Join(", ", profileResult.Errors));
            }
        }

        return results;
    }

    /// <summary>
    /// Creates profiles for ALL SuperHackers game types (Generals, ZeroHour) from manifests in the pool.
    /// This ensures that when we detect any SuperHackers placeholder and download content,
    /// profiles are created for all available game types.
    /// </summary>
    private async Task<List<ProfileOperationResult<GameProfile>>> CreateProfilesForAllSuperHackersVariantsAsync(
        GameInstallation installation,
        GameClient placeholderClient,
        ContentManifest primaryManifest,
        CancellationToken cancellationToken)
    {
        var results = new List<ProfileOperationResult<GameProfile>>();

        // Get all manifests from pool
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allManifestsResult.Success || allManifestsResult.Data == null)
        {
            logger.LogWarning("Failed to get manifests from pool for SuperHackers variant creation");
            results.Add(ProfileOperationResult<GameProfile>.CreateFailure("Could not retrieve manifests from pool"));
            return results;
        }

        // Find all SuperHackers GameClient manifests (Generals and ZeroHour)
        var superHackersManifests = allManifestsResult.Data
            .Where(m =>
                m.ContentType == ContentType.GameClient &&
                m.Id.Value.Contains(SuperHackersConstants.PublisherId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        logger.LogInformation(
            "Found {Count} SuperHackers GameClient manifests in pool: [{ManifestIds}]",
            superHackersManifests.Count,
            string.Join(", ", superHackersManifests.Select(m => m.Id.Value)));

        if (superHackersManifests.Count == 0)
        {
            // Fall back to just the primary manifest if pool lookup failed
            superHackersManifests.Add(primaryManifest);
        }

        // Create a profile for each game type
        foreach (var manifest in superHackersManifests)
        {
            var profileName = $"{installation.InstallationType} {manifest.Name}";

            // Check if profile already exists
            if (await ProfileExistsAsync(profileName, installation.Id, manifest.Id.Value, cancellationToken))
            {
                logger.LogDebug(
                    "Profile already exists for {ProfileName}, skipping",
                    profileName);
                continue;
            }

            // Create GameClient from manifest
            var gameClient = CreateGameClientFromManifest(manifest, placeholderClient, installation);

            var preferredStrategy = configService.GetDefaultWorkspaceStrategy();

            // Resolve dependencies for this game type
            var enabledContentIds = await ResolveEnabledContentAsync(
                gameClient,
                installation,
                manifest,
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
                ThemeColor = GetThemeColorForGameType(gameClient.GameType),
                IconPath = GetIconPathForGame(gameClient.GameType),
            };

            var profileResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);
            results.Add(profileResult);

            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation(
                    "Successfully created profile '{ProfileName}' for game type {ManifestId}",
                    profileResult.Data.Name,
                    manifest.Id.Value);
            }
            else
            {
                logger.LogWarning(
                    "Failed to create profile for game type {ManifestId}: {Errors}",
                    manifest.Id.Value,
                    string.Join(", ", profileResult.Errors));
            }
        }

        return results;
    }
}
