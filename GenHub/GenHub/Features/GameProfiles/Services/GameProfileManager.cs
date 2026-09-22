using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Messages;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Manages game profiles, including creation, updates, and content management.
/// </summary>
public class GameProfileManager(
    IGameProfileRepository profileRepository,
    IGameInstallationService installationService,
    IContentManifestPool manifestPool,
    IGameSettingsService gameSettingsService,
    ILogger<GameProfileManager> logger,
    ILaunchRegistry? launchRegistry = null) : IGameProfileManager
{
    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("Request cannot be null");
            }

            // Validate request
            if (!TryValidateProfileName(request.Name, out var nameValidationError))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(nameValidationError!);
            }

            // Detect if this is a Tool profile using centralized helper
            bool isToolProfile = await Core.Helpers.ToolProfileHelper.IsToolProfileAsync(
                request.EnabledContentIds ?? [],
                manifestPool,
                cancellationToken);

            string? toolContentId = null;

            if (isToolProfile)
            {
                // Validate Tool profile content configuration
                var validationError = await Core.Helpers.ToolProfileHelper.ValidateToolProfileContentAsync(
                    request.EnabledContentIds ?? [],
                    manifestPool,
                    cancellationToken);

                if (validationError != null)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(validationError);
                }

                // Set toolContentId to the single ModdingTool content ID
                toolContentId = request.EnabledContentIds!.First();

                logger.LogInformation(
                    "Detected Tool profile creation for tool: {ToolContentId}",
                    toolContentId);
            }

            // Validate based on profile type
            GameClient? gameClient = null;
            if (isToolProfile)
            {
                // Tool profile: No GameInstallation or GameClient required
                logger.LogDebug("Creating Tool profile, bypassing GameInstallation/GameClient validation");
            }
            else
            {
                // Regular profile: Require GameInstallation and GameClient
                if (string.IsNullOrWhiteSpace(request.GameInstallationId))
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure("Game installation ID is required for game profiles");
                }

                var installationResult = await installationService.GetInstallationAsync(request.GameInstallationId, cancellationToken);
                if (installationResult.Failed)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to find game installation with ID: {request.GameInstallationId}");
                }

                var gameInstallation = installationResult.Data!;

                // Use GameClient from request if provided (for provider-based clients like GeneralsOnline/SuperHackers)
                // Otherwise, look it up from AvailableGameClients (for standard installation-detected clients)
                if (request.GameClient != null)
                {
                    // Provider-based client: use the resolved game client directly
                    gameClient = request.GameClient;
                    logger.LogDebug(
                        "Using provided GameClient for profile creation: {GameClientId}",
                        gameClient.Id);
                }
                else
                {
                    // Standard client: look up from AvailableGameClients
                    gameClient = gameInstallation.AvailableGameClients.FirstOrDefault(v => v.Id == request.GameClientId);
                    if (gameClient == null)
                    {
                        return ProfileOperationResult<GameProfile>.CreateFailure($"Game client not found in installation: {request.GameClientId}");
                    }
                }
            }

            var profile = new GameProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = request.Name,
                Description = request.Description ?? string.Empty,
                GameInstallationId = request.GameInstallationId ?? string.Empty,
                GameClient = gameClient,
                WorkspaceStrategy = request.WorkspaceStrategy,
                EnabledContentIds = request.EnabledContentIds ?? [],
                ToolContentId = toolContentId, // Set for Tool profiles
                ThemeColor = request.ThemeColor,
                IconPath = request.IconPath,
                CoverPath = request.CoverPath,
                CommandLineArguments = request.CommandLineArguments ?? string.Empty,
                GameSpyIPAddress = request.GameSpyIPAddress,
                UseSteamLaunch = request.UseSteamLaunch,
            };

            // Load settings only for regular game profiles (Tool profiles don't have game settings)
            if (!isToolProfile && gameClient != null)
            {
                // Populate settings into new profile
                GameSettingsMapper.PopulateGameProfile(profile, request);

                // Load existing Options.ini settings only if they weren't explicitly provided in the request
                // This ensures we still have a baseline for unset fields but respect wizard selections.
                await LoadExistingSettingsIntoProfileAsync(profile, gameClient.GameType, cancellationToken);

                // Re-apply request settings over the loaded ones (in case LoadExistingSettingsIntoProfileAsync overwrote them)
                GameSettingsMapper.PatchGameProfile(profile, request);
            }

            var saveResult = await profileRepository.SaveProfileAsync(profile, cancellationToken);

            if (saveResult.Success)
            {
                logger.LogInformation("Successfully created game profile: {ProfileName}", profile.Name);

                // Notify listeners about the new profile
                WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(profile));
            }
            else
            {
                logger.LogError("Failed to create game profile: {ProfileName}", profile.Name);
            }

            return saveResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while creating a game profile {ProfileName}.", request?.Name);
            return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> UpdateProfileAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("Request cannot be null");
            }

            var loadResult = await profileRepository.LoadProfileAsync(profileId, cancellationToken);
            if (loadResult.Failed)
            {
                return loadResult;
            }

            var profile = loadResult.Data!;
            var previousEnabledContentIds = profile.EnabledContentIds?.ToList() ?? [];
            var previousGameClientId = profile.GameClient?.Id;

            // Check if profile is currently running
            var isRunning = await CheckIsProfileRunningAsync(profileId);
            if (isRunning)
            {
                var validationResult = await ValidateRunningProfileUpdateAsync(profileId, profile, request, previousEnabledContentIds, cancellationToken);
                if (validationResult != null)
                {
                    return validationResult;
                }
            }

            var fallbackResult = await ResolveFallbackGameClientAsync(profile, request, cancellationToken);
            if (fallbackResult != null)
            {
                return fallbackResult;
            }

            if (request.Name != null)
            {
                var nameValidationResult = ValidateAndApplyProfileName(profile, request.Name);
                if (nameValidationResult != null)
                {
                    return nameValidationResult;
                }
            }

            CheckAndHandleContentChanges(profile, request, previousEnabledContentIds, previousGameClientId, isRunning);
            ApplyUpdateRequestToProfile(profile, request);
            GameSettingsMapper.UpdateFromRequest(profile, request);

            return await SaveAndNotifyProfileUpdatedAsync(profile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while updating game profile {ProfileId}.", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return OperationResult<bool>.CreateFailure("Profile ID cannot be empty");
            }

            var profileResult = await profileRepository.LoadProfileAsync(profileId, cancellationToken);
            var profileName = profileResult.Success ? profileResult.Data?.Name ?? string.Empty : string.Empty;

            var deleteResult = await profileRepository.DeleteProfileAsync(profileId, cancellationToken);
            if (deleteResult.Success)
            {
                logger.LogInformation("Successfully deleted game profile with ID: {ProfileId}", profileId);
                try
                {
                    WeakReferenceMessenger.Default.Send(new ProfileDeletedMessage(profileId, profileName));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to publish profile deletion notification for {ProfileId}", profileId);
                }

                return OperationResult<bool>.CreateSuccess(true);
            }

            logger.LogError("Failed to delete game profile with ID: {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure(deleteResult.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting game profile {ProfileId}.", profileId);
            return OperationResult<bool>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<IReadOnlyList<GameProfile>>> GetAllProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await profileRepository.LoadAllProfilesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while getting all game profiles.");
            return ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile ID cannot be empty");
            }

            return await profileRepository.LoadProfileAsync(profileId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while getting game profile {ProfileId}.", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> GetAvailableContentAsync(GameClient gameClient, CancellationToken cancellationToken = default)
    {
        try
        {
            if (gameClient == null)
            {
                return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure("Game client cannot be null");
            }

            var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
            if (!manifestsResult.Success)
            {
                return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure(string.Join(", ", manifestsResult.Errors));
            }

            var availableContent = manifestsResult.Data!
                .Where(m => m.TargetGame == gameClient.GameType)
                .ToList();

            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess(availableContent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while getting available content for {GameType}.", gameClient?.GameType);
            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ProfileScrubResult>> ScrubDeletedManifestReferencesAsync(
        IEnumerable<string> deletedManifestIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedIdsSet = deletedManifestIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (deletedIdsSet == null || deletedIdsSet.Count == 0)
            {
                return OperationResult<ProfileScrubResult>.CreateSuccess(new ProfileScrubResult(0, 0, []));
            }

            var profilesResult = await profileRepository.LoadAllProfilesAsync(cancellationToken);
            if (!profilesResult.Success || profilesResult.Data == null)
            {
                logger.LogWarning("Failed to enumerate profiles while scrubbing deleted manifest IDs: {Error}", profilesResult.FirstError);
                return OperationResult<ProfileScrubResult>.CreateFailure(profilesResult.Errors);
            }

            var updatedCount = 0;
            var deletedCount = 0;
            var failedProfileNames = new List<string>();

            foreach (var profile in profilesResult.Data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (deleted, updated, failedName) = await ScrubSingleProfileAsync(profile, deletedIdsSet, cancellationToken);
                if (deleted)
                {
                    deletedCount++;
                }
                else if (updated)
                {
                    updatedCount++;
                }
                else if (failedName != null)
                {
                    failedProfileNames.Add(failedName);
                }
            }

            NotifyProfileListUpdatedIfChanged(logger, updatedCount, deletedCount);

            logger.LogInformation(
                "Scrubbed deleted manifest IDs: {UpdatedCount} profile(s) updated, {DeletedCount} orphaned profile(s) deleted",
                updatedCount,
                deletedCount);

            return OperationResult<ProfileScrubResult>.CreateSuccess(
                new ProfileScrubResult(updatedCount, deletedCount, failedProfileNames));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while scrubbing deleted manifest references from profiles");
            return OperationResult<ProfileScrubResult>.CreateFailure("An unexpected error occurred while scrubbing profiles.");
        }
    }

    private static ProfileOperationResult<GameProfile>? ValidateRunningProfileImmutableSettings(
        GameProfile profile,
        UpdateProfileRequest request,
        string? runningWorkspaceId)
    {
        if ((request.WorkspaceStrategy.HasValue && request.WorkspaceStrategy.Value != profile.WorkspaceStrategy) ||
            (request.ClearWorkspaceStrategy && profile.WorkspaceStrategy.HasValue))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change workspace strategy while profile is running.");
        }

        if (request.GameInstallationId != null && !string.Equals(request.GameInstallationId, profile.GameInstallationId, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change game installation while profile is running.");
        }

        if (request.ActiveWorkspaceId != null &&
            !string.Equals(request.ActiveWorkspaceId, profile.ActiveWorkspaceId, StringComparison.OrdinalIgnoreCase) &&
            !IsReconcilingWithRunningWorkspace(request.ActiveWorkspaceId, runningWorkspaceId))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change active workspace while profile is running.");
        }

        if (request.CustomExecutablePath != null && !string.Equals(request.CustomExecutablePath, profile.CustomExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change custom executable path while profile is running.");
        }

        if (request.WorkingDirectory != null && !string.Equals(request.WorkingDirectory, profile.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change working directory while profile is running.");
        }

        if (request.CommandLineArguments != null && !string.Equals(request.CommandLineArguments, profile.CommandLineArguments, StringComparison.Ordinal))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change command line arguments while profile is running.");
        }

        return null;
    }

    /// <summary>
    /// Determines whether an <see cref="UpdateProfileRequest"/> is recording the workspace that the
    /// profile's own active launch already prepared, rather than pointing the profile at a different one.
    /// </summary>
    /// <param name="requestedWorkspaceId">The workspace id carried by the update request.</param>
    /// <param name="runningWorkspaceId">The workspace id of the profile's active launch, if known.</param>
    /// <returns><c>true</c> when the request matches the running workspace.</returns>
    private static bool IsReconcilingWithRunningWorkspace(string requestedWorkspaceId, string? runningWorkspaceId)
    {
        return !string.IsNullOrEmpty(runningWorkspaceId)
            && string.Equals(requestedWorkspaceId, runningWorkspaceId, StringComparison.OrdinalIgnoreCase);
    }

    private static ProfileOperationResult<GameProfile>? ValidateRunningProfileGameClient(GameProfile profile, GameClient? requestedClient)
    {
        if (requestedClient == null)
        {
            return null;
        }

        if (profile.GameClient == null ||
            !string.Equals(requestedClient.Id, profile.GameClient.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change game client while profile is running.");
        }

        return null;
    }

    /// <summary>
    /// Validates the profile name.
    /// </summary>
    /// <param name="name">The profile name to validate.</param>
    /// <param name="errorMessage">The error message if invalid; null if valid.</param>
    /// <returns>True if valid, false otherwise.</returns>
    private static bool TryValidateProfileName(string name, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Profile name cannot be empty.";
            return false;
        }

        if (name.Length > ProfileConstants.MaxProfileNameLength)
        {
            errorMessage = "Profile name is too long.";
            return false;
        }

        // TODO: Add more rules as needed (e.g., invalid characters)
        errorMessage = null;
        return true;
    }

    private static ProfileOperationResult<GameProfile>? ValidateAndApplyProfileName(GameProfile profile, string name)
    {
        if (!TryValidateProfileName(name, out var nameValidationError))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure(nameValidationError!);
        }

        profile.Name = name;
        return null;
    }

    private static bool ReferencesDeletedManifests(GameProfile profile, HashSet<string> deletedIdsSet)
    {
        if (!string.IsNullOrWhiteSpace(profile.ToolContentId) && deletedIdsSet.Contains(profile.ToolContentId))
        {
            return true;
        }

        if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any(id => deletedIdsSet.Contains(id)))
        {
            return true;
        }

        return profile.GameClient != null &&
               !string.IsNullOrWhiteSpace(profile.GameClient.Id) &&
               deletedIdsSet.Contains(profile.GameClient.Id);
    }

    private static bool IsProfileOrphaned(
        GameProfile profile,
        HashSet<string> deletedIdsSet,
        IReadOnlyList<string> remainingContentIds)
    {
        if (!string.IsNullOrWhiteSpace(profile.ToolContentId) && deletedIdsSet.Contains(profile.ToolContentId))
        {
            return true;
        }

        if (!profile.IsToolProfile && string.IsNullOrWhiteSpace(profile.GameInstallationId))
        {
            return true;
        }

        if (remainingContentIds.Count == 0)
        {
            return true;
        }

        if (profile.GameClient != null &&
            !string.IsNullOrWhiteSpace(profile.GameClient.Id) &&
            deletedIdsSet.Contains(profile.GameClient.Id))
        {
            return true;
        }

        return IsCreatedWithContentOrphan(profile, remainingContentIds);
    }

    private static bool IsCreatedWithContentOrphan(GameProfile profile, IReadOnlyList<string> remainingContentIds)
    {
        if (profile.Description?.StartsWith(ProfileConstants.CreatedWithContentDescriptionPrefix, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        bool hasCustomContentRemaining = remainingContentIds.Any(id =>
            !id.Contains(ManifestConstants.GameInstallationManifestSegment, StringComparison.OrdinalIgnoreCase) &&
            !id.Contains(ManifestConstants.GameClientManifestSegment, StringComparison.OrdinalIgnoreCase));

        return !hasCustomContentRemaining;
    }

    private static void NotifyProfileListUpdatedIfChanged(
        ILogger logger,
        int updatedCount,
        int deletedCount)
    {
        if (deletedCount == 0 && updatedCount == 0)
        {
            return;
        }

        try
        {
            WeakReferenceMessenger.Default.Send(new ProfileListUpdatedMessage());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish profile list update notification");
        }
    }

    private static bool IsRequestedClientAvailable(IReadOnlyList<GameClient> availableClients, GameClient requestedClient)
    {
        var isAvailable = availableClients.Any(c =>
            c.IsEnabled &&
            string.Equals(c.Id, requestedClient.Id, StringComparison.OrdinalIgnoreCase));
        var isProviderClient = requestedClient.SourceType == Core.Models.Enums.ContentType.GameClient &&
                               availableClients.Any(c => c.IsEnabled && c.GameType == requestedClient.GameType);

        return isAvailable || isProviderClient;
    }

    private static GameClient? FindVersionCompatibleClient(IReadOnlyList<GameClient> availableClients, GameClient targetClient)
    {
        var hasTargetNumeric = GameVersionHelper.TryParseStrictNumericVersion(targetClient.Version, out var targetNumeric);
        return availableClients.FirstOrDefault(c =>
            c.IsEnabled &&
            c.GameType == targetClient.GameType &&
            (string.Equals(c.Version, targetClient.Version, StringComparison.OrdinalIgnoreCase) ||
             (hasTargetNumeric &&
              GameVersionHelper.TryParseStrictNumericVersion(c.Version, out var clientNumeric) &&
              clientNumeric == targetNumeric)));
    }

    private static GameClient? FindMatchingAvailableClient(
        IReadOnlyList<GameClient> availableClients,
        GameProfile profile,
        UpdateProfileRequest request)
    {
        var enabledContentIds = request.EnabledContentIds ?? profile.EnabledContentIds ?? [];
        var matchedClient = availableClients.FirstOrDefault(c =>
            c.IsEnabled && enabledContentIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase));

        if (matchedClient != null)
        {
            return matchedClient;
        }

        if (profile.GameClient != null)
        {
            return FindVersionCompatibleClient(availableClients, profile.GameClient);
        }

        if (request.EnabledContentIds == null)
        {
            return availableClients.FirstOrDefault(c => c.IsEnabled);
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve a fallback game client from the profile installation when the request omits the game client,
    /// or validates client compatibility when the profile's installation is changing.
    /// </summary>
    private async Task<ProfileOperationResult<GameProfile>?> ResolveFallbackGameClientAsync(
        GameProfile profile,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var isInstallationChanging = !string.IsNullOrEmpty(request.GameInstallationId) &&
            !string.Equals(request.GameInstallationId, profile.GameInstallationId, StringComparison.OrdinalIgnoreCase);

        if (isInstallationChanging)
        {
            return await HandleInstallationChangeClientResolutionAsync(profile, request, cancellationToken);
        }

        await ResolveClientForCurrentInstallationAsync(profile, request, cancellationToken);
        return null;
    }

    private async Task<ProfileOperationResult<GameProfile>?> HandleInstallationChangeClientResolutionAsync(
        GameProfile profile,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var newInstallationResult = await installationService.GetInstallationAsync(request.GameInstallationId!, cancellationToken);
        if (newInstallationResult is not { Success: true, Data.AvailableGameClients: not null })
        {
            return ProfileOperationResult<GameProfile>.CreateFailure(
                newInstallationResult?.Errors?.FirstOrDefault() ?? $"Failed to load installation '{request.GameInstallationId}'.");
        }

        var availableClients = newInstallationResult.Data.AvailableGameClients;
        if (request.GameClient != null)
        {
            if (!IsRequestedClientAvailable(availableClients, request.GameClient))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Game client '{request.GameClient.Id}' is not available in installation '{request.GameInstallationId}'.");
            }

            request.GameClient = request.GameClient.Clone();
            return null;
        }

        var matchedClient = FindMatchingAvailableClient(availableClients, profile, request);
        if (matchedClient != null)
        {
            request.GameClient = matchedClient.Clone();
            return null;
        }

        return ProfileOperationResult<GameProfile>.CreateFailure(
            $"No compatible game client found in installation '{request.GameInstallationId}' for profile update.");
    }

    private async Task ResolveClientForCurrentInstallationAsync(
        GameProfile profile,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var installationId = profile.GameInstallationId;
        if (request.GameClient != null || request.EnabledContentIds == null || string.IsNullOrEmpty(installationId))
        {
            return;
        }

        var installationResult = await installationService.GetInstallationAsync(installationId, cancellationToken);
        if (installationResult is { Success: true, Data.AvailableGameClients: not null })
        {
            var availableClients = installationResult.Data.AvailableGameClients;
            GameClient? matchedClient = null;

            if (profile.GameClient != null &&
                request.EnabledContentIds.Contains(profile.GameClient.Id, StringComparer.OrdinalIgnoreCase))
            {
                matchedClient = availableClients.FirstOrDefault(c =>
                    c.IsEnabled && string.Equals(c.Id, profile.GameClient.Id, StringComparison.OrdinalIgnoreCase));
            }

            matchedClient ??= availableClients.FirstOrDefault(c =>
                c.IsEnabled && request.EnabledContentIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase));

            if (matchedClient != null)
            {
                request.GameClient = matchedClient.Clone();
            }
        }
    }

    /// <summary>
    /// Loads existing Options.ini settings and populates the profile with them.
    /// This ensures new profiles inherit existing game settings.
    /// </summary>
    private async Task LoadExistingSettingsIntoProfileAsync(
        GameProfile profile,
        Core.Models.Enums.GameType gameType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Loading existing Options.ini for {GameType} to populate new profile {ProfileName}", gameType, profile.Name);

            var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
            if (loadResult.Success && loadResult.Data != null)
            {
                var options = loadResult.Data;

                // Map Options.ini settings to profile
                GameSettingsMapper.ApplyFromOptions(options, profile);

                logger.LogInformation("Populated profile {ProfileName} with existing Options.ini settings", profile.Name);
            }
            else
            {
                logger.LogDebug("No existing Options.ini found for {GameType}, profile {ProfileName} will use defaults", gameType, profile.Name);
            }

            // If this is a GeneralsOnline profile, also inherit existing settings.json settings
            if (profile.IsGeneralsOnlineProfile())
            {
                logger.LogDebug("Loading existing GeneralsOnline settings.json to populate new profile {ProfileName}", profile.Name);
                var goLoadResult = await gameSettingsService.LoadGeneralsOnlineSettingsAsync(cancellationToken);
                if (goLoadResult.Success && goLoadResult.Data != null)
                {
                    GameSettingsMapper.ApplyFromGeneralsOnlineSettings(goLoadResult.Data, profile);
                    logger.LogInformation("Populated profile {ProfileName} with existing GeneralsOnline settings", profile.Name);
                }
            }
        }
        catch (Exception ex)
        {
            // Don't fail profile creation if settings loading fails
            logger.LogWarning(ex, "Failed to load existing Options.ini for profile {ProfileName}, using defaults", profile.Name);
        }
    }

    /// <summary>
    /// Gets the workspace id of the profile's active launch, if one is registered.
    /// </summary>
    /// <param name="profileId">The profile id to inspect.</param>
    /// <returns>The active launch's workspace id, or <c>null</c> when none is available.</returns>
    private async Task<string?> GetActiveLaunchWorkspaceIdAsync(string profileId)
    {
        if (launchRegistry == null)
        {
            return null;
        }

        var activeLaunches = await launchRegistry.GetAllActiveLaunchesAsync();
        var launch = activeLaunches.FirstOrDefault(l =>
            string.Equals(l.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) && !l.TerminatedAt.HasValue);
        return string.IsNullOrEmpty(launch?.WorkspaceId) ? null : launch.WorkspaceId;
    }

    private async Task<bool> CheckIsProfileRunningAsync(string profileId)
    {
        if (launchRegistry == null)
        {
            return false;
        }

        var activeLaunches = await launchRegistry.GetAllActiveLaunchesAsync();
        return activeLaunches.Any(l => string.Equals(l.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) && !l.TerminatedAt.HasValue);
    }

    private async Task<ProfileOperationResult<GameProfile>?> ValidateRunningProfileUpdateAsync(
        string profileId,
        GameProfile profile,
        UpdateProfileRequest request,
        List<string> previousEnabledContentIds,
        CancellationToken cancellationToken)
    {
        if (request.IsRollback)
        {
            logger.LogInformation(
                "UpdateProfileAsync for running profile {ProfileId} is flagged as rollback; bypassing running profile immutability validation to restore snapshot.",
                profileId);
            return null;
        }

        var runningWorkspaceId = await GetActiveLaunchWorkspaceIdAsync(profileId);
        return await ValidateRunningProfileUpdateRequestAsync(profile, request, previousEnabledContentIds, runningWorkspaceId, cancellationToken);
    }

    private async Task<ProfileOperationResult<GameProfile>> SaveAndNotifyProfileUpdatedAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        var saveResult = await profileRepository.SaveProfileAsync(profile, cancellationToken);
        if (saveResult.Success)
        {
            logger.LogInformation("Successfully updated game profile: {ProfileName}", profile.Name);

            // Send notification after successful update so UI can refresh
            // This is critical for GameProfileLauncherViewModel.RefreshSingleProfileAsync to work
            try
            {
                WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(profile));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish profile update notification for {ProfileId}", profile.Id);
            }
        }
        else
        {
            logger.LogError("Failed to update game profile: {ProfileName}", profile.Name);
        }

        return saveResult;
    }

    private async Task<ProfileOperationResult<GameProfile>?> ValidateRunningProfileUpdateRequestAsync(
        GameProfile profile,
        UpdateProfileRequest request,
        List<string> previousEnabledContentIds,
        string? runningWorkspaceId,
        CancellationToken cancellationToken)
    {
        var settingsError = ValidateRunningProfileImmutableSettings(profile, request, runningWorkspaceId);
        if (settingsError != null)
        {
            return settingsError;
        }

        var clientError = ValidateRunningProfileGameClient(profile, request.GameClient);
        if (clientError != null)
        {
            return clientError;
        }

        return await ValidateRunningProfileContentChangesAsync(previousEnabledContentIds, request.EnabledContentIds, cancellationToken);
    }

    private async Task<ProfileOperationResult<GameProfile>?> ValidateRunningProfileContentChangesAsync(
        List<string> previousEnabledContentIds,
        List<string>? requestedContentIds,
        CancellationToken cancellationToken)
    {
        if (requestedContentIds == null)
        {
            return null;
        }

        var newContentIds = requestedContentIds.ToList();
        var addedIds = newContentIds.Except(previousEnabledContentIds, StringComparer.OrdinalIgnoreCase).ToList();
        var removedIds = previousEnabledContentIds.Except(newContentIds, StringComparer.OrdinalIgnoreCase).ToList();
        var changedIds = addedIds.Concat(removedIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var id in changedIds)
        {
            if (!ManifestId.TryCreate(id, out var manifestId))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Cannot modify content '{id}' while profile is running: invalid manifest ID format.");
            }

            var manifestResult = await manifestPool.GetManifestAsync(manifestId, cancellationToken);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Cannot modify content '{id}' while profile is running: manifest not found.");
            }

            var manifest = manifestResult.Data;
            if (!ContentHotswapClassification.IsHotswappable(manifest))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Cannot modify content '{manifest.Name}' while profile is running. Only content targeting user documents (such as maps and replays) can be hot swapped during an active game session.");
            }
        }

        return null;
    }

    private void ApplyUpdateRequestToProfile(GameProfile profile, UpdateProfileRequest request)
    {
        profile.Description = request.Description ?? profile.Description;
        profile.EnabledContentIds = request.EnabledContentIds ?? profile.EnabledContentIds ?? [];

        if (request.GameClient != null)
        {
            profile.GameClient = request.GameClient;
        }

        profile.WorkspaceStrategy = request.ClearWorkspaceStrategy
            ? null
            : request.WorkspaceStrategy ?? profile.WorkspaceStrategy;
        profile.LaunchOptions = request.LaunchArguments ?? profile.LaunchOptions ?? [];
        profile.CustomExecutablePath = request.CustomExecutablePath ?? profile.CustomExecutablePath;
        profile.WorkingDirectory = request.WorkingDirectory ?? profile.WorkingDirectory;
        profile.IconPath = request.IconPath ?? profile.IconPath;
        profile.CoverPath = request.CoverPath ?? profile.CoverPath;
        profile.ThemeColor = request.ThemeColor ?? profile.ThemeColor;
        profile.GameInstallationId = request.GameInstallationId ?? profile.GameInstallationId;
        profile.ToolContentId = request.ToolContentId ?? profile.ToolContentId;
        profile.CommandLineArguments = request.CommandLineArguments ?? profile.CommandLineArguments;

        if (request.UseSteamLaunch.HasValue)
        {
            profile.UseSteamLaunch = request.UseSteamLaunch.Value;
        }

        if (request.ActiveWorkspaceId != null)
        {
            profile.ActiveWorkspaceId = request.ActiveWorkspaceId;
        }
    }

    private void CheckAndHandleContentChanges(
        GameProfile profile,
        UpdateProfileRequest request,
        List<string> previousEnabledContentIds,
        string? previousGameClientId,
        bool isRunning)
    {
        bool contentChanged = false;
        if (request.EnabledContentIds != null)
        {
            var newContentIds = request.EnabledContentIds.ToList();
            contentChanged = !previousEnabledContentIds.SequenceEqual(newContentIds, StringComparer.OrdinalIgnoreCase);
        }

        if (request.GameClient != null)
        {
            var newGameClientId = request.GameClient.Id;
            contentChanged = contentChanged || !string.Equals(previousGameClientId, newGameClientId, StringComparison.OrdinalIgnoreCase);
        }

        if (contentChanged && !isRunning && !string.IsNullOrEmpty(profile.ActiveWorkspaceId))
        {
            logger.LogDebug(
                "Profile '{ProfileName}' content changed - clearing ActiveWorkspaceId '{WorkspaceId}' to force workspace rebuild on next launch",
                profile.Name,
                profile.ActiveWorkspaceId);
            profile.ActiveWorkspaceId = string.Empty;
        }
    }

    private async Task<(bool Deleted, bool Updated, string? FailedName)> ScrubSingleProfileAsync(
        GameProfile profile,
        HashSet<string> deletedIdsSet,
        CancellationToken cancellationToken)
    {
        if (!ReferencesDeletedManifests(profile, deletedIdsSet))
        {
            return (false, false, null);
        }

        var remainingContentIds = profile.EnabledContentIds?
            .Where(id => !deletedIdsSet.Contains(id))
            .ToList() ?? [];

        if (IsProfileOrphaned(profile, deletedIdsSet, remainingContentIds))
        {
            logger.LogInformation("Deleting orphaned profile {ProfileName} ({ProfileId}) because its referenced manifests were deleted", profile.Name, profile.Id);
            var deleteResult = await DeleteProfileAsync(profile.Id, cancellationToken);
            if (deleteResult.Success)
            {
                return (true, false, null);
            }

            logger.LogWarning("Failed to delete orphaned profile {ProfileName} ({ProfileId}): {Error}", profile.Name, profile.Id, deleteResult.FirstError);
            return (false, false, profile.Name);
        }

        logger.LogInformation("Scrubbing deleted manifest IDs from profile {ProfileName} ({ProfileId})", profile.Name, profile.Id);
        var updateRequest = new UpdateProfileRequest
        {
            EnabledContentIds = remainingContentIds,
        };

        var updateResult = await UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
        if (updateResult.Success)
        {
            return (false, true, null);
        }

        logger.LogWarning("Failed to update scrubbed profile {ProfileName} ({ProfileId}): {Error}", profile.Name, profile.Id, updateResult.FirstError);
        return (false, false, profile.Name);
    }
}
