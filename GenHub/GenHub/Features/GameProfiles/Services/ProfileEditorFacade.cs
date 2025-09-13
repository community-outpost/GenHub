using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Facade for game profile editing operations, coordinating between multiple services
/// to provide a simplified interface for profile management.
/// </summary>
public class ProfileEditorFacade : IProfileEditorFacade
{
    private readonly IGameProfileManager _profileManager;
    private readonly IContentOrchestrator _contentOrchestrator;
    private readonly IGameInstallationService _installationService;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IContentManifestPool _manifestPool;
    private readonly IConfigurationProviderService _config;
    private readonly ILogger<ProfileEditorFacade> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileEditorFacade"/> class.
    /// </summary>
    /// <param name="profileManager">The game profile manager.</param>
    /// <param name="contentOrchestrator">The content orchestrator.</param>
    /// <param name="installationService">The game installation service.</param>
    /// <param name="workspaceManager">The workspace manager.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="config">The configuration provider service.</param>
    /// <param name="logger">The logger instance.</param>
    public ProfileEditorFacade(
        IGameProfileManager profileManager,
        IContentOrchestrator contentOrchestrator,
        IGameInstallationService installationService,
        IWorkspaceManager workspaceManager,
        IContentManifestPool manifestPool,
        IConfigurationProviderService config,
        ILogger<ProfileEditorFacade> logger)
    {
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _contentOrchestrator = contentOrchestrator ?? throw new ArgumentNullException(nameof(contentOrchestrator));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileWithWorkspaceAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating profile with workspace for game installation: {InstallationId}", request.GameInstallationId);

            // First create the profile
            var createResult = await _profileManager.CreateProfileAsync(request, cancellationToken);
            if (createResult.Failed)
            {
                return createResult;
            }

            var profile = createResult.Data!;

            // Discover available content for this profile
            var contentResult = await _profileManager.GetAvailableContentAsync(profile.GameVersion, cancellationToken);
            if (contentResult.Success && contentResult.Data != null)
            {
                // Auto-enable some basic content (game installation content)
                var gameInstallationContent = contentResult.Data
                    .Where(c => c.ContentType == Core.Models.Enums.ContentType.GameInstallation)
                    .ToList();

                if (gameInstallationContent.Any())
                {
                    profile.EnabledContentIds = gameInstallationContent.Select(c => c.Id.ToString()).ToList();

                    // Update the profile with enabled content
                    var updateRequest = new UpdateProfileRequest
                    {
                        EnabledContentIds = profile.EnabledContentIds,
                    };

                    var updateResult = await _profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                    if (updateResult.Success)
                    {
                        profile = updateResult.Data!;
                    }
                }
            }

            // Prepare workspace for the profile
            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profile.Id,
                Manifests = new List<ContentManifest>(),
                GameVersion = profile.GameVersion,
                Strategy = profile.WorkspaceStrategy,
                ForceRecreate = false,
                ValidateAfterPreparation = true,
            };

            // resolve installation path and workspace root
            var install = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (install.Failed || install.Data == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Failed to resolve installation '{profile.GameInstallationId}': {install.FirstError}");
            }

            workspaceConfig.BaseInstallationPath = install.Data.InstallationPath;
            workspaceConfig.WorkspaceRootPath = _config.GetWorkspacePath();

            // Build manifests from enabled content IDs
            if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any())
            {
                var manifests = new List<ContentManifest>();
                foreach (var contentId in profile.EnabledContentIds)
                {
                    try
                    {
                        var manifestResult = await _manifestPool.GetManifestAsync(contentId, cancellationToken);
                        if (manifestResult.Success && manifestResult.Data != null)
                        {
                            manifests.Add(manifestResult.Data);
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Skip invalid manifest IDs
                        _logger.LogWarning("Skipping invalid manifest ID: {ContentId}", contentId);
                    }
                }

                workspaceConfig.Manifests = manifests;
            }

            var workspaceResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
            if (workspaceResult.Success && workspaceResult.Data != null)
            {
                profile.ActiveWorkspaceId = workspaceResult.Data.Id;

                // persist ActiveWorkspaceId
                await _profileManager.UpdateProfileAsync(profile.Id, new UpdateProfileRequest(), cancellationToken);
                _logger.LogInformation("Created workspace {WorkspaceId} for profile {ProfileId}", workspaceResult.Data.Id, profile.Id);
            }

            _logger.LogInformation("Successfully created profile {ProfileId} with workspace", profile.Id);
            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile with workspace");
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to create profile with workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> UpdateProfileWithWorkspaceAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating profile {ProfileId} with workspace refresh", profileId);

            // Update the profile
            var updateResult = await _profileManager.UpdateProfileAsync(profileId, request, cancellationToken);
            if (updateResult.Failed)
            {
                return updateResult;
            }

            var profile = updateResult.Data!;

            // If content changed, refresh the workspace
            if (request.EnabledContentIds != null)
            {
                var workspaceConfig = new WorkspaceConfiguration
                {
                    Id = profileId,
                    Manifests = new List<ContentManifest>(),
                    GameVersion = profile.GameVersion,
                    Strategy = profile.WorkspaceStrategy,
                    ForceRecreate = true, // Force recreate since content changed
                    ValidateAfterPreparation = true,
                };

                // resolve installation path and workspace root
                var install = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
                if (install.Failed || install.Data == null)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(
                        $"Failed to resolve installation '{profile.GameInstallationId}': {install.FirstError}");
                }

                workspaceConfig.BaseInstallationPath = install.Data.InstallationPath;
                workspaceConfig.WorkspaceRootPath = _config.GetWorkspacePath();

                // Build manifests from enabled content IDs
                if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any())
                {
                    var manifests = new List<ContentManifest>();
                    foreach (var contentId in profile.EnabledContentIds)
                    {
                        try
                        {
                            var manifestResult = await _manifestPool.GetManifestAsync(contentId, cancellationToken);
                            if (manifestResult.Success && manifestResult.Data != null)
                            {
                                manifests.Add(manifestResult.Data);
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Skip invalid manifest IDs
                            _logger.LogWarning("Skipping invalid manifest ID: {ContentId}", contentId);
                        }
                    }

                    workspaceConfig.Manifests = manifests;
                }

                var workspaceResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
                if (workspaceResult.Success && workspaceResult.Data != null)
                {
                    profile.ActiveWorkspaceId = workspaceResult.Data.Id;

                    // persist ActiveWorkspaceId
                    await _profileManager.UpdateProfileAsync(profile.Id, new UpdateProfileRequest(), cancellationToken);
                    _logger.LogInformation("Refreshed workspace {WorkspaceId} for profile {ProfileId}", workspaceResult.Data.Id, profile.Id);
                }
            }

            _logger.LogInformation("Successfully updated profile {ProfileId} with workspace", profileId);
            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile {ProfileId} with workspace", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to update profile with workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> GetProfileWithWorkspaceAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting profile {ProfileId} with workspace information", profileId);

            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return profileResult;
            }

            var profile = profileResult.Data!;

            // Get workspace information if available
            if (!string.IsNullOrEmpty(profile.ActiveWorkspaceId))
            {
                var allWorkspacesResult = await _workspaceManager.GetAllWorkspacesAsync(cancellationToken);
                if (allWorkspacesResult.Success && allWorkspacesResult.Data != null)
                {
                    var workspace = allWorkspacesResult.Data.FirstOrDefault(w => w.Id == profile.ActiveWorkspaceId);
                    if (workspace != null)
                    {
                        // Could enrich profile with workspace info here
                        _logger.LogDebug("Profile {ProfileId} has active workspace {WorkspaceId}", profileId, profile.ActiveWorkspaceId);
                    }
                }
            }

            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get profile {ProfileId} with workspace", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to get profile with workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> DiscoverContentForVersionAsync(string gameVersionId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering content for game version: {GameVersionId}", gameVersionId);

            // Get all manifests from the pool
            var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
            if (manifestsResult.Failed)
            {
                return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure(string.Join(", ", manifestsResult.Errors));
            }

            // Filter by game version (this is a simplified implementation)
            // In a real implementation, you'd need to map gameVersionId to GameType
            var relevantContent = manifestsResult.Data?.ToList() ?? new List<ContentManifest>();

            _logger.LogInformation("Discovered {Count} content items for game version {GameVersionId}", relevantContent.Count, gameVersionId);
            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess(relevantContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover content for game version {GameVersionId}", gameVersionId);
            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure($"Failed to discover content: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> ValidateProfileAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating profile {ProfileId}", profile.Id);

            var errors = new List<string>();

            // Basic validation
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                errors.Add("Profile name is required");
            }

            if (string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                errors.Add("Game installation is required");
            }

            // Validate that the game installation exists
            if (!string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                var installationResult = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
                if (installationResult.Failed)
                {
                    errors.Add($"Game installation not found: {profile.GameInstallationId}");
                }
            }

            // Validate content manifests exist
            if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any())
            {
                var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
                if (manifestsResult.Success && manifestsResult.Data != null)
                {
                    var availableManifestIds = manifestsResult.Data.Select(m => m.Id.ToString()).ToHashSet();
                    var missingContent = profile.EnabledContentIds.Where(id => !availableManifestIds.Contains(id)).ToList();

                    if (missingContent.Any())
                    {
                        errors.Add($"Content manifests not found: {string.Join(", ", missingContent)}");
                    }
                }
            }

            if (errors.Any())
            {
                _logger.LogWarning("Profile {ProfileId} validation failed: {Errors}", profile.Id, string.Join(", ", errors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            _logger.LogDebug("Profile {ProfileId} validation successful", profile.Id);
            return ProfileOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate profile {ProfileId}", profile?.Id);
            return ProfileOperationResult<bool>.CreateFailure($"Profile validation failed: {ex.Message}");
        }
    }
}
