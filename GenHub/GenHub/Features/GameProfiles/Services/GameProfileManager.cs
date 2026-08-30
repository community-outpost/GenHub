using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

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
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile name cannot be empty");
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
                    request.EnabledContentIds,
                    manifestPool,
                    cancellationToken);

                if (validationError != null)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(validationError);
                }

                // Set toolContentId to the single ModdingTool content ID
                toolContentId = request.EnabledContentIds.First();

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
            };

            // Load settings only for regular game profiles (Tool profiles don't have game settings)
            if (!isToolProfile && gameClient != null)
            {
                // Populate settings into new profile
                GameSettingsMapper.PopulateGameProfile(profile, request);

                // Load existing Options.ini settings only if they weren't explicitly provided in the request
                // This ensures we still have a baseline for unset fields but respect wizard selections.
                await LoadExistingSettingsIntoProfileAsync(profile, gameClient.GameType);

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
            var isRunning = false;
            if (launchRegistry != null)
            {
                var activeLaunches = await launchRegistry.GetAllActiveLaunchesAsync();
                isRunning = activeLaunches.Any(l => string.Equals(l.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) && !l.TerminatedAt.HasValue);
            }

            if (isRunning)
            {
                var validationResult = await ValidateRunningProfileUpdateRequestAsync(profile, request, previousEnabledContentIds, cancellationToken);
                if (validationResult != null)
                {
                    return validationResult;
                }
            }

            if (request.Name != null)
            {
                if (!TryValidateProfileName(request.Name, out var nameValidationError))
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(nameValidationError!);
                }

                profile.Name = request.Name;
            }

            CheckAndHandleContentChanges(profile, request, previousEnabledContentIds, previousGameClientId, isRunning);
            ApplyUpdateRequestToProfile(profile, request);
            GameSettingsMapper.UpdateFromRequest(profile, request);

            var saveResult = await profileRepository.SaveProfileAsync(profile, cancellationToken);
            if (saveResult.Success)
            {
                logger.LogInformation("Successfully updated game profile: {ProfileName}", profile.Name);

                // Send notification after successful update so UI can refresh
                // This is critical for GameProfileLauncherViewModel.RefreshSingleProfileAsync to work
                WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(profile));
            }
            else
            {
                logger.LogError("Failed to update game profile: {ProfileName}", profile.Name);
            }

            return saveResult;
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

            var deleteResult = await profileRepository.DeleteProfileAsync(profileId, cancellationToken);
            if (deleteResult.Success)
            {
                logger.LogInformation("Successfully deleted game profile with ID: {ProfileId}", profileId);
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

    private static ProfileOperationResult<GameProfile>? ValidateRunningProfileImmutableSettings(GameProfile profile, UpdateProfileRequest request)
    {
        if (request.WorkspaceStrategy.HasValue && request.WorkspaceStrategy.Value != profile.WorkspaceStrategy)
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change workspace strategy while profile is running.");
        }

        if (request.GameInstallationId != null && !string.Equals(request.GameInstallationId, profile.GameInstallationId, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("Cannot change game installation while profile is running.");
        }

        if (request.ActiveWorkspaceId != null && !string.Equals(request.ActiveWorkspaceId, profile.ActiveWorkspaceId, StringComparison.OrdinalIgnoreCase))
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

    private static ProfileOperationResult<GameProfile>? ValidateRunningProfileGameClient(GameProfile profile, GameClient? requestedClient)
    {
        if (requestedClient == null)
        {
            return null;
        }

        if (profile.GameClient == null ||
            !string.Equals(requestedClient.Id, profile.GameClient.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestedClient.ExecutablePath, profile.GameClient.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestedClient.Version, profile.GameClient.Version, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestedClient.WorkingDirectory, profile.GameClient.WorkingDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestedClient.InstallationId, profile.GameClient.InstallationId, StringComparison.OrdinalIgnoreCase) ||
            requestedClient.GameType != profile.GameClient.GameType)
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

        if (name.Length > 100)
        {
            errorMessage = "Profile name is too long.";
            return false;
        }

        // TODO: Add more rules as needed (e.g., invalid characters)
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Loads existing Options.ini settings and populates the profile with them.
    /// This ensures new profiles inherit existing game settings.
    /// </summary>
    private async Task LoadExistingSettingsIntoProfileAsync(GameProfile profile, Core.Models.Enums.GameType gameType)
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
        }
        catch (Exception ex)
        {
            // Don't fail profile creation if settings loading fails
            logger.LogWarning(ex, "Failed to load existing Options.ini for profile {ProfileName}, using defaults", profile.Name);
        }
    }

    private async Task<ProfileOperationResult<GameProfile>?> ValidateRunningProfileUpdateRequestAsync(
        GameProfile profile,
        UpdateProfileRequest request,
        List<string> previousEnabledContentIds,
        CancellationToken cancellationToken)
    {
        var settingsError = ValidateRunningProfileImmutableSettings(profile, request);
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
        profile.GameClient = request.GameClient ?? profile.GameClient;
        profile.WorkspaceStrategy = request.WorkspaceStrategy ?? profile.WorkspaceStrategy;
        profile.LaunchOptions = request.LaunchArguments ?? profile.LaunchOptions ?? [];
        profile.CustomExecutablePath = request.CustomExecutablePath ?? profile.CustomExecutablePath;
        profile.WorkingDirectory = request.WorkingDirectory ?? profile.WorkingDirectory;
        profile.IconPath = request.IconPath ?? profile.IconPath;
        profile.CoverPath = request.CoverPath ?? profile.CoverPath;
        profile.ThemeColor = request.ThemeColor ?? profile.ThemeColor;
        profile.GameInstallationId = request.GameInstallationId ?? profile.GameInstallationId;
        profile.ToolContentId = request.ToolContentId ?? profile.ToolContentId;
        profile.CommandLineArguments = request.CommandLineArguments ?? profile.CommandLineArguments;

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
}
