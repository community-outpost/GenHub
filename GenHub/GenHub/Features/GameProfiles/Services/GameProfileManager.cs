using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
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
    ILogger<GameProfileManager> logger) : IGameProfileManager
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
                    request.EnabledContentIds!,
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
                GameClient = gameClient ?? new(),
                WorkspaceStrategy = request.PreferredStrategy,
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

            if (request.Name != null)
            {
                if (!TryValidateProfileName(request.Name, out var nameValidationError))
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(nameValidationError!);
                }

                profile.Name = request.Name;
            }

            profile.Description = request.Description ?? profile.Description;
            profile.EnabledContentIds = request.EnabledContentIds ?? profile.EnabledContentIds;
            profile.WorkspaceStrategy = request.PreferredStrategy ?? profile.WorkspaceStrategy;
            profile.LaunchOptions = request.LaunchArguments ?? profile.LaunchOptions;
            profile.CustomExecutablePath = request.CustomExecutablePath ?? profile.CustomExecutablePath;
            profile.WorkingDirectory = request.WorkingDirectory ?? profile.WorkingDirectory;
            profile.IconPath = request.IconPath ?? profile.IconPath;
            profile.CoverPath = request.CoverPath ?? profile.CoverPath;
            profile.ThemeColor = request.ThemeColor ?? profile.ThemeColor;
            profile.GameInstallationId = request.GameInstallationId ?? profile.GameInstallationId;
            profile.ToolContentId = request.ToolContentId ?? profile.ToolContentId;
            profile.CommandLineArguments = request.CommandLineArguments ?? profile.CommandLineArguments;

            // Only update ActiveWorkspaceId if explicitly provided (not null or empty)
            if (!string.IsNullOrEmpty(request.ActiveWorkspaceId))
            {
                profile.ActiveWorkspaceId = request.ActiveWorkspaceId;
            }

            // Update game settings
            GameSettingsMapper.UpdateFromRequest(profile, request);

            var saveResult = await profileRepository.SaveProfileAsync(profile, cancellationToken);
            if (saveResult.Success)
            {
                logger.LogInformation("Successfully updated game profile: {ProfileName}", profile.Name);
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
            else
            {
                logger.LogError("Failed to delete game profile with ID: {ProfileId}", profileId);
                return OperationResult<bool>.CreateFailure(deleteResult.Errors);
            }
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
}
