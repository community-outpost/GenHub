using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
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
    ILogger<GameProfileManager> logger) : IGameProfileManager
{
    private readonly IGameProfileRepository _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
    private readonly IGameInstallationService _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly ILogger<GameProfileManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

            var installationResult = await _installationService.GetInstallationAsync(request.GameInstallationId, cancellationToken);
            if (installationResult.Failed)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to find game installation with ID: {request.GameInstallationId}");
            }

            var gameInstallation = installationResult.Data!;
            var gameClient = gameInstallation.AvailableGameClients.FirstOrDefault(v => v.Id == request.GameClientId);
            if (gameClient == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Game client not found in installation: {request.GameClientId}");
            }

            var profile = new GameProfile
            {
                Name = request.Name ?? string.Empty,
                Description = request.Description ?? string.Empty,
                GameInstallationId = gameInstallation.Id,
                GameClient = gameClient,
                WorkspaceStrategy = request.PreferredStrategy,
            };

            var saveResult = await _profileRepository.SaveProfileAsync(profile, cancellationToken);

            if (saveResult.Success)
            {
                _logger.LogInformation("Successfully created game profile: {ProfileName}", profile.Name);
            }
            else
            {
                _logger.LogError("Failed to create game profile: {ProfileName}", profile.Name);
            }

            return saveResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while creating a game profile {ProfileName}.", request?.Name);
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

            var loadResult = await _profileRepository.LoadProfileAsync(profileId, cancellationToken);
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
            profile.ThemeColor = request.ThemeColor ?? profile.ThemeColor;

            var saveResult = await _profileRepository.SaveProfileAsync(profile, cancellationToken);
            if (saveResult.Success)
            {
                _logger.LogInformation("Successfully updated game profile: {ProfileName}", profile.Name);
            }
            else
            {
                _logger.LogError("Failed to update game profile: {ProfileName}", profile.Name);
            }

            return saveResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while updating game profile {ProfileId}.", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile ID cannot be empty");
            }

            var deleteResult = await _profileRepository.DeleteProfileAsync(profileId, cancellationToken);
            if (deleteResult.Success)
            {
                _logger.LogInformation("Successfully deleted game profile with ID: {ProfileId}", profileId);
            }
            else
            {
                _logger.LogError("Failed to delete game profile with ID: {ProfileId}", profileId);
            }

            return deleteResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while deleting game profile {ProfileId}.", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<IReadOnlyList<GameProfile>>> GetAllProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _profileRepository.LoadAllProfilesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while getting all game profiles.");
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

            return await _profileRepository.LoadProfileAsync(profileId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while getting game profile {ProfileId}.", profileId);
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

            var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
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
            _logger.LogError(ex, "An unexpected error occurred while getting available content for {GameType}.", gameClient?.GameType);
            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Validates the profile name.
    /// </summary>
    /// <param name="name">The profile name to validate.</param>
    /// <param name="errorMessage">The error message if invalid; null if valid.</param>
    /// <returns>True if valid, false otherwise.</returns>
    private bool TryValidateProfileName(string name, out string? errorMessage)
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
}
