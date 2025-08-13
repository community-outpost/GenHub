using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services
{
    /// <summary>
    /// Manages game profiles, including creation, updates, and content management.
    /// </summary>
    public class GameProfileManager : IGameProfileManager
    {
        private readonly IGameProfileRepository _profileRepository;
        private readonly IGameInstallationService _installationService;
        private readonly IContentManifestPool _manifestPool;
        private readonly ILogger<GameProfileManager> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameProfileManager"/> class.
        /// </summary>
        /// <param name="profileRepository">The profile repository.</param>
        /// <param name="installationService">The installation service.</param>
        /// <param name="manifestPool">The manifest pool.</param>
        /// <param name="logger">The logger.</param>
        public GameProfileManager(
            IGameProfileRepository profileRepository,
            IGameInstallationService installationService,
            IContentManifestPool manifestPool,
            ILogger<GameProfileManager> logger)
        {
            _profileRepository = profileRepository;
            _installationService = installationService;
            _manifestPool = manifestPool;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<ProfileOperationResult<GameProfile>> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
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
                var gameVersion = gameInstallation.AvailableVersions.FirstOrDefault(v => v.Id == request.GameVersionId);
                if (gameVersion == null)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure($"Game version not found in installation: {request.GameVersionId}");
                }

                var profile = new GameProfile
                {
                    Name = request.Name,
                    Description = request.Description,
                    GameInstallationId = gameInstallation.Id,
                    GameVersionId = gameVersion.Id,
                    GameVersion = gameVersion,
                    PreferredStrategy = request.PreferredStrategy,
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
                _logger.LogError(ex, "An unexpected error occurred while creating a game profile.");
                return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
            }
        }

        /// <inheritdoc/>
        public async Task<ProfileOperationResult<GameProfile>> UpdateProfileAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var loadResult = await _profileRepository.LoadProfileAsync(profileId, cancellationToken);
                if (loadResult.Failed)
                {
                    return loadResult;
                }

                var profile = loadResult.Data!;

                if (request.Name != null) profile.Name = request.Name;
                if (request.Description != null) profile.Description = request.Description;
                if (request.EnabledContentIds != null) profile.EnabledContentIds = request.EnabledContentIds;
                if (request.PreferredStrategy.HasValue) profile.PreferredStrategy = request.PreferredStrategy.Value;
                if (request.LaunchArguments != null) profile.LaunchArguments = request.LaunchArguments;
                if (request.EnvironmentVariables != null) profile.EnvironmentVariables = request.EnvironmentVariables;
                if (request.CustomExecutablePath != null) profile.CustomExecutablePath = request.CustomExecutablePath;
                if (request.WorkingDirectory != null) profile.WorkingDirectory = request.WorkingDirectory;
                if (request.IsActive.HasValue) profile.IsActive = request.IsActive.Value;
                if (request.IconPath != null) profile.IconPath = request.IconPath;
                if (request.ThemeColor != null) profile.ThemeColor = request.ThemeColor;

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
                _logger.LogError(ex, "An unexpected error occurred while updating a game profile.");
                return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
            }
        }

        /// <inheritdoc/>
        public async Task<ProfileOperationResult<GameProfile>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            try
            {
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
                _logger.LogError(ex, "An unexpected error occurred while deleting a game profile.");
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
                return await _profileRepository.LoadProfileAsync(profileId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while getting a game profile.");
                return ProfileOperationResult<GameProfile>.CreateFailure("An unexpected error occurred.");
            }
        }

        /// <inheritdoc/>
        public async Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> GetAvailableContentAsync(GameVersion gameVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
                if (!manifestsResult.Success)
                {
                    return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure(string.Join(", ", manifestsResult.Errors));
                }

                var availableContent = manifestsResult.Data!
                    .Where(m => m.TargetGame == gameVersion.GameType)
                    .ToList();

                return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess(availableContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while getting available content.");
                return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure("An unexpected error occurred.");
            }
        }
    }
}
