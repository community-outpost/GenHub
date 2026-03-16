using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    IContentOrchestrator contentOrchestrator,
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
            var previousIds = new HashSet<string>(enabledContentIds, StringComparer.OrdinalIgnoreCase);
            try
            {
                var resolvedIds = await dependencyResolver.ResolveDependenciesAsync(enabledContentIds, cancellationToken);
                enabledContentIds = [.. resolvedIds];

                // Ensure the target manifest is included (may have been added by resolution)
                if (!enabledContentIds.Contains(manifestId, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Add(manifestId);
                }

                // Notify user if dependencies were auto-installed
                var newlyAdded = enabledContentIds
                    .Where(id => !previousIds.Contains(id))
                    .ToList();

                if (newlyAdded.Count > 0)
                {
                    var dependencyNames = new List<string>();
                    foreach (var id in newlyAdded)
                    {
                        try
                        {
                            // Auto-acquire missing dependencies when possible
                            if (!await TryAcquireDependencyAsync(id, cancellationToken))
                            {
                                logger.LogWarning("Dependency {DependencyId} could not be auto-acquired", id);
                            }

                            var depManifest = await manifestPool.GetManifestAsync(
                                Core.Models.Manifest.ManifestId.Create(id),
                                cancellationToken);

                            if (depManifest.Success && depManifest.Data != null)
                            {
                                dependencyNames.Add(depManifest.Data.Name ?? "Required dependency");
                            }
                            else if (TryParseCommunityOutpostContentCode(id, out var contentCode))
                            {
                                var metadata = Core.Models.CommunityOutpost.GenPatcherContentRegistry.GetMetadata(contentCode);
                                dependencyNames.Add(!string.IsNullOrEmpty(metadata.DisplayName)
                                    ? metadata.DisplayName
                                    : "Required dependency");
                            }
                            else
                            {
                                dependencyNames.Add("Required dependency");
                            }
                        }
                        catch
                        {
                            dependencyNames.Add("Required dependency");
                        }
                    }

                    logger.LogInformation("Auto-installed {Count} dependencies for {ManifestId}", newlyAdded.Count, manifestId);
                    notificationService.ShowInfo(
                        "Dependencies Added",
                        $"Added required dependencies for '{contentName}': {string.Join(", ", dependencyNames)}");
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
            return AddToProfileResult.CreateFailure("Content not found. Please download it again and retry.", sw.Elapsed);
        }
        catch (ManifestValidationException ex)
        {
            logger.LogWarning("Content {ManifestId} validation failed: {Message}", manifestId, ex.Message);
            return AddToProfileResult.CreateFailure("Content validation failed. Please re-download and retry.", sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Add content operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add content {ManifestId} to profile {ProfileId}", manifestId, profileId);
            return AddToProfileResult.CreateFailure("Failed to add content. Please try again.", sw.Elapsed);
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
            if (ExclusiveContentTypes.Contains(newManifest.ContentType))
            {
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
            }

            // Check for Community Outpost category-specific conflicts (hotkeys, control bars, cameras)
            // These addons are mutually exclusive within their category
            var newContentCode = GetContentCodeFromManifest(newManifest);
            if (!string.IsNullOrEmpty(newContentCode))
            {
                var conflictingCodes = Core.Models.CommunityOutpost.GenPatcherDependencyBuilder.GetConflictingCodes(newContentCode);
                if (conflictingCodes.Count > 0)
                {
                    // Check if any conflicting content is enabled
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
                                var existingContentCode = GetContentCodeFromManifest(existingManifest);

                                if (!string.IsNullOrEmpty(existingContentCode) &&
                                    conflictingCodes.Contains(existingContentCode, StringComparer.OrdinalIgnoreCase))
                                {
                                    // Found a conflict - return conflict info
                                    logger.LogInformation(
                                        "Content conflict detected: {NewContent} ({NewCode}) conflicts with {ExistingContent} ({ExistingCode})",
                                        newManifest.Name,
                                        newContentCode,
                                        existingManifest.Name,
                                        existingContentCode);

                                    return ContentConflictInfo.ExclusiveContentConflict(
                                        existingId,
                                        existingManifest.Name,
                                        existingManifest.ContentType);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Failed to check manifest {ExistingId} for category conflicts", existingId);
                        }
                    }
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
            return ProfileOperationResult<GameProfile>.CreateFailure("Content not found. Please download it again and retry.");
        }
        catch (ManifestValidationException ex)
        {
            logger.LogWarning("Content {ManifestId} validation failed: {Message}", manifestId, ex.Message);
            return ProfileOperationResult<GameProfile>.CreateFailure("Content validation failed. Please re-download and retry.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Create profile operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create profile '{ProfileName}' with content {ManifestId}", profileName, manifestId);
            return ProfileOperationResult<GameProfile>.CreateFailure("Failed to create profile. Please try again.");
        }
    }

    /// <summary>
    /// Validates a profile's enabled content for conflicts.
    /// Returns a list of conflict warnings to display to the user.
    /// </summary>
    /// <param name="profileId">The profile ID to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of conflict warning messages.</returns>
    public async Task<List<string>> ValidateProfileContentAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        try
        {
            // Get the profile
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed || profileResult.Data == null)
            {
                return warnings;
            }

            var profile = profileResult.Data;
            var enabledIds = profile.EnabledContentIds?.ToList() ?? [];

            // Check each pair of enabled content for conflicts
            for (int i = 0; i < enabledIds.Count; i++)
            {
                for (int j = i + 1; j < enabledIds.Count; j++)
                {
                    try
                    {
                        var manifest1Result = await manifestPool.GetManifestAsync(
                            Core.Models.Manifest.ManifestId.Create(enabledIds[i]),
                            cancellationToken);

                        var manifest2Result = await manifestPool.GetManifestAsync(
                            Core.Models.Manifest.ManifestId.Create(enabledIds[j]),
                            cancellationToken);

                        if (manifest1Result.Success && manifest1Result.Data != null &&
                            manifest2Result.Success && manifest2Result.Data != null)
                        {
                            var manifest1 = manifest1Result.Data;
                            var manifest2 = manifest2Result.Data;

                            // Check exclusive content type conflicts
                            if (ExclusiveContentTypes.Contains(manifest1.ContentType) &&
                                manifest1.ContentType == manifest2.ContentType)
                            {
                                warnings.Add($"⚠ Conflict: '{manifest1.Name}' and '{manifest2.Name}' cannot both be enabled ({manifest1.ContentType})");
                            }

                            // Check Community Outpost category conflicts
                            var code1 = GetContentCodeFromManifest(manifest1);
                            var code2 = GetContentCodeFromManifest(manifest2);

                            if (!string.IsNullOrEmpty(code1) && !string.IsNullOrEmpty(code2))
                            {
                                var conflicting1 = Core.Models.CommunityOutpost.GenPatcherDependencyBuilder.GetConflictingCodes(code1);
                                if (conflicting1.Contains(code2, StringComparer.OrdinalIgnoreCase))
                                {
                                    warnings.Add($"⚠ Conflict: '{manifest1.Name}' and '{manifest2.Name}' cannot both be enabled. Please remove one.");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to check conflict between {Id1} and {Id2}", enabledIds[i], enabledIds[j]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error validating profile content for {ProfileId}", profileId);
        }

        return warnings;
    }

    private static bool TryParseCommunityOutpostContentCode(string manifestId, out string contentCode)
    {
        contentCode = string.Empty;
        var parts = manifestId.Split('.');

        if (parts.Length < 5 ||
            !parts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var codePart = parts[4];
        contentCode = codePart.Length >= 4 ? codePart[..4] : codePart;
        return !string.IsNullOrEmpty(contentCode);
    }

    /// <summary>
    /// Extracts the content code from a manifest's metadata tags.
    /// Used for Community Outpost content conflict detection.
    /// </summary>
    /// <param name="manifest">The manifest to extract the content code from.</param>
    /// <returns>The content code, or empty string if not found.</returns>
    private static string GetContentCodeFromManifest(Core.Models.Manifest.ContentManifest manifest)
    {
        // Look for contentCode tag in metadata
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(t => t.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(contentCodeTag))
        {
            return contentCodeTag["contentCode:".Length..];
        }

        // Try to extract from manifest ID
        // Format: 1.version.communityoutpost.contentType.contentName
        var idParts = manifest.Id.Value?.Split('.') ?? [];
        if (idParts.Length >= 5)
        {
            // Community Outpost uses language suffixes (e.g., hleienglish)
            if (idParts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
            {
                var codePart = idParts[4];
                return codePart.Length >= 4 ? codePart[..4] : codePart;
            }

            return idParts[4];
        }

        return string.Empty;
    }

    private async Task<bool> TryAcquireDependencyAsync(string manifestId, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (existing.Success && existing.Data != null)
            {
                return true;
            }

            if (!TryParseCommunityOutpostContentCode(manifestId, out var contentCode))
            {
                return false;
            }

            var query = new ContentSearchQuery
            {
                ProviderName = CommunityOutpostConstants.PublisherId,
                SearchTerm = contentCode,
                IncludeInstalled = true,
                Take = 50,
            };

            var searchResult = await contentOrchestrator.SearchAsync(query, cancellationToken);
            if (searchResult.Failed || searchResult.Data == null)
            {
                return false;
            }

            var match = searchResult.Data.FirstOrDefault(r =>
                r.Id.EndsWith($".{contentCode}", StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return false;
            }

            var acquireResult = await contentOrchestrator.AcquireContentAsync(match, null, cancellationToken);
            return acquireResult.Success;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to auto-acquire dependency {ManifestId}", manifestId);
            return false;
        }
    }
}
