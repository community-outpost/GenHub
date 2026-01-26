using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for managing content-to-profile operations including adding content,
/// detecting conflicts, and creating profiles with pre-enabled content.
/// </summary>
public sealed class ProfileContentService(
    IGameProfileManager profileManager,
    IContentManifestPool manifestPool,
    IDependencyResolver dependencyResolver,
    IGameInstallationService installationService,
    INotificationService notificationService,
    ILogger<ProfileContentService> logger) : IProfileContentService
{
    /// <summary>
    /// Content types that are exclusive (only one can be enabled at a time per profile).
    /// </summary>
    private static readonly HashSet<ContentType> ExclusiveContentTypes =
    [
        ContentType.GameClient,
        ContentType.GameInstallation,
    ];

    /// <inheritdoc/>
    public async Task<AddToProfileResult> AddContentToProfileAsync(
        string profileId,
        string manifestId,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("Adding content {ManifestId} to profile {ProfileId}", manifestId, profileId);

            // Get the profile
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed || profileResult.Data == null)
            {
                var error = profileResult.FirstError ?? "Profile not found";
                logger.LogWarning("Failed to get profile {ProfileId}: {Error}", profileId, error);
                return AddToProfileResult.CreateFailure(error, sw.Elapsed);
            }

            var profile = profileResult.Data;

            // Get the manifest to add
            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (manifestResult.Failed || manifestResult.Data == null)
            {
                var error = manifestResult.FirstError ?? "Failed to retrieve manifest";
                logger.LogWarning("Failed to get manifest {ManifestId}: {Error}", manifestId, error);
                return AddToProfileResult.CreateFailure(error, sw.Elapsed);
            }

            var manifest = manifestResult.Data;
            var contentName = manifest.Name ?? manifestId;

            // Check for conflicts
            var conflictInfo = await CheckContentConflictsAsync(profileId, manifestId, cancellationToken);

            // Build new enabled content list
            List<string> enabledContentIds = [.. profile.EnabledContentIds ?? []];
            string? swappedContentId = null;
            string? swappedContentName = null;
            ContentType swappedContentType = ContentType.UnknownContentType;

            if (conflictInfo.HasConflict && conflictInfo.CanAutoResolve)
            {
                // Remove the conflicting content
                if (!string.IsNullOrEmpty(conflictInfo.ConflictingContentId))
                {
                    enabledContentIds.Remove(conflictInfo.ConflictingContentId);
                    swappedContentId = conflictInfo.ConflictingContentId;
                    swappedContentName = conflictInfo.ConflictingContentName;
                    swappedContentType = conflictInfo.ConflictingContentType;

                    logger.LogInformation(
                        "Swapping content: removing {OldContent} to add {NewContent}",
                        swappedContentId,
                        manifestId);
                }
            }

            // Add the new content if not already present
            if (!enabledContentIds.Contains(manifestId, StringComparer.OrdinalIgnoreCase))
            {
                enabledContentIds.Add(manifestId);
            }

            // Resolve dependencies
            try
            {
                var resolvedIds = await dependencyResolver.ResolveDependenciesAsync(enabledContentIds, cancellationToken);
                enabledContentIds = [.. resolvedIds];

                // Ensure the target manifest is included (may have been added by resolution)
                if (!enabledContentIds.Contains(manifestId, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Add(manifestId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve dependencies, proceeding with original list");
            }

            // Update the profile
            var updateRequest = new UpdateProfileRequest
            {
                EnabledContentIds = enabledContentIds,
            };

            var updateResult = await profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
            if (updateResult.Failed)
            {
                var error = updateResult.FirstError ?? "Failed to update profile";
                logger.LogError("Failed to update profile {ProfileId}: {Error}", profileId, error);
                return AddToProfileResult.CreateFailure(error, sw.Elapsed);
            }

            // Show notification for swap
            if (!string.IsNullOrEmpty(swappedContentId))
            {
                notificationService.ShowInfo(
                    "Content Replaced",
                    $"Replaced '{swappedContentName ?? swappedContentId}' with '{contentName}'");

                logger.LogInformation(
                    "Content swap complete: {OldContent} → {NewContent} in profile {ProfileId}",
                    swappedContentId,
                    manifestId,
                    profileId);

                return AddToProfileResult.CreateSuccessWithSwap(
                    manifestId,
                    contentName,
                    swappedContentId,
                    swappedContentName,
                    swappedContentType,
                    sw.Elapsed);
            }

            logger.LogInformation(
                "Successfully added content {ManifestId} to profile {ProfileId}",
                manifestId,
                profileId);

            return AddToProfileResult.CreateSuccess(manifestId, contentName, sw.Elapsed);
        }
        catch (ManifestNotFoundException ex)
        {
            logger.LogWarning("Content {ManifestId} not found: {Message}", manifestId, ex.Message);
            return AddToProfileResult.CreateFailure($"Content not found: {ex.Message}", sw.Elapsed);
        }
        catch (ManifestValidationException ex)
        {
            logger.LogWarning("Content {ManifestId} validation failed: {Message}", manifestId, ex.Message);
            return AddToProfileResult.CreateFailure($"Validation failed: {ex.Message}", sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Add content operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add content {ManifestId} to profile {ProfileId}", manifestId, profileId);
            return AddToProfileResult.CreateFailure($"Failed to add content: {ex.Message}", sw.Elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<ContentConflictInfo> CheckContentConflictsAsync(
        string profileId,
        string manifestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Checking conflicts for adding {ManifestId} to profile {ProfileId}", manifestId, profileId);

            // Get the profile
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed || profileResult.Data == null)
            {
                return ContentConflictInfo.NoConflict();
            }

            var profile = profileResult.Data;

            // Get the manifest to add
            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (manifestResult.Failed || manifestResult.Data == null)
            {
                return ContentConflictInfo.NoConflict();
            }

            var newManifest = manifestResult.Data;

            // Check if this is an exclusive content type
            if (!ExclusiveContentTypes.Contains(newManifest.ContentType))
            {
                return ContentConflictInfo.NoConflict();
            }

            // Check for existing content of the same exclusive type
            foreach (var existingId in profile.EnabledContentIds ?? [])
            {
                try
                {
                    var existingResult = await manifestPool.GetManifestAsync(
                        Core.Models.Manifest.ManifestId.Create(existingId),
                        cancellationToken);

                    if (existingResult.Success && existingResult.Data != null)
                    {
                        var existingManifest = existingResult.Data;

                        if (existingManifest.ContentType == newManifest.ContentType)
                        {
                            // Same exclusive type - conflict
                            if (newManifest.ContentType == ContentType.GameClient)
                            {
                                return ContentConflictInfo.GameClientConflict(
                                    existingId,
                                    existingManifest.Name);
                            }

                            return ContentConflictInfo.ExclusiveContentConflict(
                                existingId,
                                existingManifest.Name,
                                existingManifest.ContentType);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to check manifest {ExistingId} for conflicts", existingId);
                }
            }

            return ContentConflictInfo.NoConflict();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking conflicts for {ManifestId}", manifestId);
            return ContentConflictInfo.NoConflict();
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileWithContentAsync(
        string profileName,
        string manifestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Creating new profile '{ProfileName}' with content {ManifestId}", profileName, manifestId);

            // Get the manifest to determine game type
            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (manifestResult.Failed || manifestResult.Data == null)
            {
                var error = manifestResult.FirstError ?? "Failed to retrieve manifest";
                return ProfileOperationResult<GameProfile>.CreateFailure(error);
            }

            var manifest = manifestResult.Data;

            // Build enabled content IDs with dependency resolution
            List<string> enabledContentIds = [manifestId];
            try
            {
                var resolvedIds = await dependencyResolver.ResolveDependenciesAsync(enabledContentIds, cancellationToken);
                enabledContentIds = [.. resolvedIds];

                // Ensure the target manifest is included (may have been added by resolution)
                if (!enabledContentIds.Contains(manifestId, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Add(manifestId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve dependencies for new profile, proceeding with original content");
            }

            // Find a suitable game installation
            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (installationsResult.Failed || installationsResult.Data == null || installationsResult.Data.Count == 0)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("No game installations found. Please configure a game installation first.");
            }

            // Find installation that has a game client matching the content's target game type
            var installation = installationsResult.Data.FirstOrDefault(i =>
                i.AvailableGameClients.Any(c => c.GameType == manifest.TargetGame)) ?? installationsResult.Data[0];

            if (installation.AvailableGameClients.Count == 0)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"No game clients found for installation '{installation.InstallationType}'.");
            }

            // Prefer a game client matching the target game type
            var gameClient = installation.AvailableGameClients.FirstOrDefault(c => c.GameType == manifest.TargetGame)
                ?? installation.AvailableGameClients.FirstOrDefault();

            if (gameClient == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"No suitable game client found for installation '{installation.InstallationType}'.");
            }

            // Standalone content (Tools, Addons, Executables) does not require a GameInstallation or GameClient foundation.
            // We skip adding these foundation manifests to the profile if the target content is standalone.
            if (manifest.ContentType.IsStandalone())
            {
                logger.LogInformation("Creating standalone profile for {ManifestId} - skipping foundation injection", manifestId);
            }
            else
            {
                // Generate and add the GameInstallation manifest ID to enabled content
                var gameInstallationManifestId = Core.Models.Manifest.ManifestIdGenerator.GenerateGameInstallationId(
                    installation,
                    manifest.TargetGame,
                    gameClient.Version); // Use the actual game version from the selected client

                if (!enabledContentIds.Contains(gameInstallationManifestId, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Insert(0, gameInstallationManifestId); // Add at beginning for proper dependency order
                    logger.LogInformation("Added GameInstallation manifest {ManifestId} to enabled content", gameInstallationManifestId);
                }

                // Add the GameClient manifest ID only if the content being added is not a GameClient
                // (e.g., if adding a mod/mappack, we need the base game client; if adding GeneralsOnline, we don't)
                if (manifest.ContentType != ContentType.GameClient &&
                    !string.IsNullOrEmpty(gameClient.Id) &&
                    !enabledContentIds.Contains(gameClient.Id, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Insert(1, gameClient.Id); // Add after GameInstallation
                    logger.LogInformation("Added GameClient manifest {ManifestId} to enabled content", gameClient.Id);
                }
                else if (manifest.ContentType == ContentType.GameClient)
                {
                    logger.LogInformation("Skipping base GameClient - content being added is already a GameClient: {ManifestId}", manifestId);
                }
            }

            // Create the profile request
            var createRequest = new CreateProfileRequest
            {
                Name = profileName,
                GameInstallationId = installation.Id,
                GameClientId = gameClient.Id,
                EnabledContentIds = enabledContentIds,
                Description = $"Profile created with {manifest.Name}",
            };

            // Create the profile
            var createResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);
            if (createResult.Failed)
            {
                var error = createResult.FirstError ?? "Failed to create profile";
                logger.LogError("Failed to create profile '{ProfileName}': {Error}", profileName, error);
                return createResult;
            }

            notificationService.ShowSuccess(
                "Profile Created",
                $"Created profile '{profileName}' with {manifest.Name}");

            logger.LogInformation(
                "Successfully created profile {ProfileId} with content {ManifestId}",
                createResult.Data!.Id,
                manifestId);

            return createResult;
        }
        catch (ManifestNotFoundException ex)
        {
            logger.LogWarning("Content {ManifestId} not found: {Message}", manifestId, ex.Message);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Content not found: {ex.Message}");
        }
        catch (ManifestValidationException ex)
        {
            logger.LogWarning("Content {ManifestId} validation failed: {Message}", manifestId, ex.Message);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Validation failed: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Create profile operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create profile '{ProfileName}' with content {ManifestId}", profileName, manifestId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to create profile: {ex.Message}");
        }
    }
}
